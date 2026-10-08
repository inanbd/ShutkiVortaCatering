namespace ShutkiVorta.Application.Common.Interfaces;

public interface IImageStorage
{
    /// <summary>Stores an uploaded menu image and returns its public URL path.</summary>
    Task<string> SaveMenuImageAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    /// <summary>Deletes a previously uploaded image. URLs not created by this storage are ignored.</summary>
    Task DeleteAsync(string? url, CancellationToken cancellationToken = default);
}
