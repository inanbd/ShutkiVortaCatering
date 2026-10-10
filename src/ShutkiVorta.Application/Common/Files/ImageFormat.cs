namespace ShutkiVorta.Application.Common.Files;

/// <summary>A photo format recognised from the file's first bytes (its signature), so renamed non-image files are rejected.</summary>
public sealed record ImageFormat(string Extension, string ContentType)
{
    public static readonly ImageFormat Jpeg = new(".jpg", "image/jpeg");
    public static readonly ImageFormat Png = new(".png", "image/png");
    public static readonly ImageFormat Webp = new(".webp", "image/webp");

    /// <summary>Reads the signature and rewinds the stream. Returns null for anything but JPEG, PNG or WebP.</summary>
    public static async Task<ImageFormat?> DetectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        if (read < header.Length)
        {
            return null;
        }

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return Jpeg;
        }

        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
        {
            return Png;
        }

        if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
        {
            return Webp;
        }

        return null;
    }
}
