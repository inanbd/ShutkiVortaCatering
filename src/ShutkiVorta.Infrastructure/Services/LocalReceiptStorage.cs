using System.Text.RegularExpressions;
using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Infrastructure.Services;

/// <summary>
/// Keeps receipt photos in a private folder (Inventory:ReceiptsPath, default App_Data/receipts), outside wwwroot so they are
/// never served as public files. Admins view them through the admin pages. Back the folder up together with the database.
/// </summary>
internal sealed partial class LocalReceiptStorage(string folder) : IReceiptStorage
{
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        if (!OwnFileName().IsMatch(fileName))
        {
            throw new ArgumentException($"Receipts cannot be stored as '{extension}' files.", nameof(extension));
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
            await content.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            File.Delete(path); // never leave half-written photos behind
            throw;
        }

        return fileName;
    }

    public Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (!OwnFileName().IsMatch(fileName))
        {
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            Stream stream = new FileStream(
                Path.Combine(folder, fileName), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 81920, useAsync: true);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        // Only names this storage generated, so a value from the database can never point outside the folder.
        var path = Path.Combine(folder, fileName);
        if (OwnFileName().IsMatch(fileName) && File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)$")]
    private static partial Regex OwnFileName();
}
