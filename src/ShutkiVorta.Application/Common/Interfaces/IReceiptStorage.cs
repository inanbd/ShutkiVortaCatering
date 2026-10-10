namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>Private storage for receipt photos. Unlike menu photos they are never public; admins see them through the app.</summary>
public interface IReceiptStorage
{
    /// <summary>Stores a receipt photo and returns the generated file name to keep in the database.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    /// <summary>Opens a stored receipt for reading, or returns null when the file is missing.</summary>
    Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a stored receipt. Missing files and names this storage did not generate are ignored.</summary>
    Task DeleteAsync(string fileName, CancellationToken cancellationToken = default);
}
