using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Inventory;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Inventory;

/// <summary>Deletes a purchase with its receipt photos. Saved item names are kept.</summary>
public sealed record DeleteInventoryPurchaseCommand(int Id) : IRequest, IRequireAdmin;

/// <summary>Saves a new item name (when <see cref="Id"/> is null), or renames an item and changes its usual unit. Returns the id.</summary>
public sealed record SaveInventoryItemCommand(int? Id, string Name, string Unit) : IRequest<int>, IRequireAdmin;

/// <summary>Deletes a saved item name that no purchase uses, e.g. one saved with a typo.</summary>
public sealed record DeleteInventoryItemCommand(int Id) : IRequest, IRequireAdmin;

public sealed class SaveInventoryItemCommandValidator : AbstractValidator<SaveInventoryItemCommand>
{
    public SaveInventoryItemCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(InventoryItem.MaxNameLength);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(InventoryItem.MaxUnitLength);
    }
}

internal sealed class InventoryCommandHandlers(
    IInventoryRepository repository,
    IReceiptStorage storage,
    IDateTimeProvider clock,
    ILogger<InventoryCommandHandlers> logger) :
    IRequestHandler<DeleteInventoryPurchaseCommand>,
    IRequestHandler<SaveInventoryItemCommand, int>,
    IRequestHandler<DeleteInventoryItemCommand>
{
    public async Task Handle(DeleteInventoryPurchaseCommand request, CancellationToken cancellationToken)
    {
        var purchase = await repository.GetPurchaseAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Inventory purchase", request.Id);

        await repository.DeletePurchaseAsync(purchase.Id, cancellationToken);
        await storage.DeleteQuietlyAsync(purchase.Receipts.Select(r => r.FileName), logger);
    }

    public async Task<int> Handle(SaveInventoryItemCommand request, CancellationToken cancellationToken)
    {
        string normalized;
        try
        {
            normalized = InventoryItem.Normalize(request.Name);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(nameof(SaveInventoryItemCommand.Name), ex.Message);
        }

        var sameName = await repository.GetItemByNormalizedNameAsync(normalized, cancellationToken);
        if (sameName is not null && sameName.Id != request.Id)
        {
            throw new ValidationException(nameof(SaveInventoryItemCommand.Name), $"\"{sameName.Name}\" is already saved.");
        }

        try
        {
            if (request.Id is { } id)
            {
                var item = await repository.GetItemAsync(id, cancellationToken) ?? throw new NotFoundException("Inventory item", id);
                item.Update(request.Name, request.Unit, clock.UtcNow);
                await repository.UpdateItemAsync(item, cancellationToken);
                return item.Id;
            }

            var created = InventoryItem.Create(request.Name, request.Unit, clock.UtcNow);
            await repository.AddItemAsync(created, cancellationToken);
            return created.Id;
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }
    }

    public async Task Handle(DeleteInventoryItemCommand request, CancellationToken cancellationToken)
    {
        var item = await repository.GetItemAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Inventory item", request.Id);

        var uses = await repository.CountPurchasesWithItemAsync(item.Id, cancellationToken);
        if (uses > 0)
        {
            throw new ValidationException(
                $"\"{item.Name}\" is on {uses} purchase{(uses == 1 ? string.Empty : "s")}, so it cannot be deleted. If the name is wrong, rename it instead.");
        }

        await repository.DeleteItemAsync(item.Id, cancellationToken);
    }
}
