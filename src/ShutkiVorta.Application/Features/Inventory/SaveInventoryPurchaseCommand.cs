using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Files;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Inventory;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Inventory;

/// <summary>A receipt photo chosen in the form.</summary>
public sealed record ReceiptUpload(Stream Content, string FileName, long Length);

/// <summary>
/// Records what was added to the inventory (when <see cref="Id"/> is null) or corrects an earlier entry. Item names are
/// saved for suggestions the first time they are used. Returns the purchase id.
/// </summary>
public sealed record SaveInventoryPurchaseCommand : IRequest<int>, IRequireAdmin
{
    public const long MaxReceiptBytes = 10 * 1024 * 1024;

    public int? Id { get; init; }
    public DateOnly PurchasedOn { get; init; }
    public string? Store { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<Line> Lines { get; init; } = [];
    public IReadOnlyList<ReceiptUpload> NewReceipts { get; init; } = [];
    public IReadOnlyList<int> RemoveReceiptIds { get; init; } = [];

    /// <summary>One item: its name, the quantity bought and the total price paid for that quantity.</summary>
    public sealed record Line(string? ItemName, decimal? Quantity, string? Unit, decimal? Price);
}

public sealed class SaveInventoryPurchaseCommandValidator : AbstractValidator<SaveInventoryPurchaseCommand>
{
    public SaveInventoryPurchaseCommandValidator()
    {
        RuleFor(x => x.Store).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one item with its quantity and price.");
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(InventoryPurchase.MaxLines)
            .OverridePropertyName("Lines").WithMessage($"A purchase can have at most {InventoryPurchase.MaxLines} items. Please split it into two.");
        RuleFor(x => x.NewReceipts.Count).LessThanOrEqualTo(InventoryPurchase.MaxReceipts)
            .OverridePropertyName("Receipts").WithMessage($"Please upload at most {InventoryPurchase.MaxReceipts} receipt photos.");

        // One message per problem, naming the item, so the admin can find the row on a long receipt.
        RuleFor(x => x.Lines).Custom((lines, context) =>
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var label = string.IsNullOrWhiteSpace(line.ItemName) ? $"Item {i + 1}" : line.ItemName.Trim();

                if (string.IsNullOrWhiteSpace(line.ItemName))
                {
                    context.AddFailure($"Lines[{i}].ItemName", "One of the rows has a quantity or price but no item name.");
                }
                else if (line.ItemName.Trim().Length > InventoryItem.MaxNameLength)
                {
                    context.AddFailure($"Lines[{i}].ItemName", $"{label[..40]}…: item names can be at most {InventoryItem.MaxNameLength} characters.");
                }

                if (line.Quantity is not > 0 || line.Quantity > InventoryPurchaseLine.MaxQuantity)
                {
                    context.AddFailure($"Lines[{i}].Quantity", $"{label}: please enter the quantity bought.");
                }

                if (string.IsNullOrWhiteSpace(line.Unit) || line.Unit.Trim().Length > InventoryItem.MaxUnitLength)
                {
                    context.AddFailure($"Lines[{i}].Unit", $"{label}: please enter a unit such as lb, bag or bottle (up to {InventoryItem.MaxUnitLength} characters).");
                }

                if (line.Price is null or < 0 || line.Price > InventoryPurchaseLine.MaxPrice)
                {
                    context.AddFailure($"Lines[{i}].Price", $"{label}: please enter the price paid (0 if it was free).");
                }
            }
        });
    }
}

internal sealed class SaveInventoryPurchaseCommandHandler(
    IInventoryRepository repository,
    IReceiptStorage storage,
    IDateTimeProvider clock,
    ICurrentUser currentUser,
    ILogger<SaveInventoryPurchaseCommandHandler> logger) : IRequestHandler<SaveInventoryPurchaseCommand, int>
{
    public async Task<int> Handle(SaveInventoryPurchaseCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.BusinessNow);
        var lines = request.Lines.Select(l => new InventoryLineRequest(l.ItemName!, l.Quantity!.Value, l.Unit!, l.Price!.Value)).ToList();

        InventoryPurchase purchase;
        List<InventoryReceipt> removed;
        try
        {
            if (request.Id is { } id)
            {
                purchase = await repository.GetPurchaseAsync(id, cancellationToken) ?? throw new NotFoundException("Inventory purchase", id);
                purchase.Update(request.PurchasedOn, request.Store, request.Notes, lines, today, now);
            }
            else
            {
                purchase = InventoryPurchase.Record(request.PurchasedOn, request.Store, request.Notes, lines, currentUser.Email ?? "Admin", today, now);
            }

            removed = request.RemoveReceiptIds.Distinct().Select(r => purchase.RemoveReceipt(r, now)).OfType<InventoryReceipt>().ToList();
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        // Check every photo before storing any, so a bad file never leaves the others behind.
        var uploads = await CheckUploadsAsync(request.NewReceipts, purchase.Receipts.Count, cancellationToken);

        var stored = new List<string>();
        try
        {
            foreach (var (upload, format) in uploads)
            {
                var fileName = await storage.SaveAsync(upload.Content, format.Extension, cancellationToken);
                stored.Add(fileName);
                purchase.AddReceipt(fileName, upload.FileName, format.ContentType, upload.Length, now);
            }

            if (purchase.IsTransient)
            {
                await repository.AddPurchaseAsync(purchase, cancellationToken);
            }
            else
            {
                await repository.UpdatePurchaseAsync(purchase, cancellationToken);
            }
        }
        catch
        {
            // Nothing was saved, so the photos just stored would never be shown or cleaned up.
            await storage.DeleteQuietlyAsync(stored, logger);
            throw;
        }

        await storage.DeleteQuietlyAsync(removed.Select(r => r.FileName), logger);
        return purchase.Id;
    }

    private static async Task<List<(ReceiptUpload Upload, ImageFormat Format)>> CheckUploadsAsync(
        IReadOnlyList<ReceiptUpload> uploads, int existingCount, CancellationToken cancellationToken)
    {
        if (existingCount + uploads.Count > InventoryPurchase.MaxReceipts)
        {
            throw new ValidationException("Receipts",
                $"A purchase can have at most {InventoryPurchase.MaxReceipts} receipt photos. It already has {existingCount}; remove some or upload fewer.");
        }

        var checkedUploads = new List<(ReceiptUpload, ImageFormat)>();
        foreach (var upload in uploads)
        {
            var name = string.IsNullOrWhiteSpace(upload.FileName) ? "A receipt photo" : $"\"{upload.FileName}\"";
            if (upload.Length <= 0)
            {
                throw new ValidationException("Receipts", $"{name} is empty.");
            }

            if (upload.Length > SaveInventoryPurchaseCommand.MaxReceiptBytes)
            {
                throw new ValidationException("Receipts", $"{name} is larger than 10 MB. Please upload a smaller photo.");
            }

            var format = await ImageFormat.DetectAsync(upload.Content, cancellationToken)
                ?? throw new ValidationException("Receipts", $"{name} is not a JPG, PNG or WebP photo.");
            checkedUploads.Add((upload, format));
        }

        return checkedUploads;
    }
}

internal static class ReceiptStorageExtensions
{
    /// <summary>Deletes receipt files, logging (not throwing) when one cannot be removed: the database is already up to date.</summary>
    public static async Task DeleteQuietlyAsync(this IReceiptStorage storage, IEnumerable<string> fileNames, ILogger logger)
    {
        foreach (var fileName in fileNames)
        {
            try
            {
                await storage.DeleteAsync(fileName, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not delete receipt file {FileName}", fileName);
            }
        }
    }
}
