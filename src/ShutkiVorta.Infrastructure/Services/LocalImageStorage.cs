using Microsoft.AspNetCore.Hosting;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Infrastructure.Services;

/// <summary>Stores uploaded menu photos under wwwroot/uploads/menu. Swap for blob storage in a scaled-out deployment.</summary>
internal sealed class LocalImageStorage(IWebHostEnvironment environment) : IImageStorage
{
    private const string PublicFolder = "/uploads/menu/";

    public async Task<string> SaveMenuImageAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(WebRoot, "uploads", "menu");
        Directory.CreateDirectory(directory);

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using var file = File.Create(Path.Combine(directory, fileName));
        await content.CopyToAsync(file, cancellationToken);

        return PublicFolder + fileName;
    }

    public Task DeleteAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(PublicFolder, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var fileName = Path.GetFileName(url);
        var path = Path.Combine(WebRoot, "uploads", "menu", fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string WebRoot => environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
}
