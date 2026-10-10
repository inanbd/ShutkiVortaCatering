using MediatR;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Files;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Features.Menu;

public sealed record SetMenuItemAvailabilityCommand(int Id, bool IsAvailable) : IRequest, IRequireAdmin;

public sealed record DeleteMenuItemCommand(int Id) : IRequest, IRequireAdmin;

/// <summary>Validates and stores an uploaded menu photo; returns its public URL.</summary>
public sealed record UploadMenuImageCommand(Stream Content, string FileName, long Length) : IRequest<string>, IRequireAdmin;

internal sealed class MenuItemCommandHandlers(
    IMenuItemRepository repository,
    IImageStorage imageStorage,
    IDateTimeProvider clock,
    IPublisher publisher) :
    IRequestHandler<SetMenuItemAvailabilityCommand>,
    IRequestHandler<DeleteMenuItemCommand>,
    IRequestHandler<UploadMenuImageCommand, string>
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task Handle(SetMenuItemAvailabilityCommand request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Menu item", request.Id);
        item.SetAvailability(request.IsAvailable, clock.UtcNow);
        await repository.UpdateAsync(item, cancellationToken);
        await publisher.Publish(new MenuChangedNotification(item.Id), cancellationToken);
    }

    public async Task Handle(DeleteMenuItemCommand request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Menu item", request.Id);

        if (await repository.IsReferencedByOrdersAsync(item.Id, cancellationToken))
        {
            throw new ValidationException(
                $"\"{item.Name}\" appears on existing orders, so it cannot be deleted. Mark it as unavailable instead.");
        }

        await repository.DeleteAsync(item.Id, cancellationToken);
        await imageStorage.DeleteAsync(item.ImageUrl, cancellationToken);
        await publisher.Publish(new MenuChangedNotification(item.Id), cancellationToken);
    }

    public async Task<string> Handle(UploadMenuImageCommand request, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new ValidationException("ImageFile", "Please upload a JPG, PNG or WebP image.");
        }

        if (request.Length is <= 0 or > MaxImageBytes)
        {
            throw new ValidationException("ImageFile", "Images must be smaller than 5 MB.");
        }

        if (await ImageFormat.DetectAsync(request.Content, cancellationToken) is null)
        {
            throw new ValidationException("ImageFile", "The uploaded file is not a valid image.");
        }

        return await imageStorage.SaveMenuImageAsync(request.Content, extension, cancellationToken);
    }
}
