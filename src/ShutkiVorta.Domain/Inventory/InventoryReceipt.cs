using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Inventory;

/// <summary>A photo of the receipt for a purchase. The file itself lives in private storage under <see cref="FileName"/>.</summary>
public sealed class InventoryReceipt : Entity
{
    // Required by the data mapper.
    private InventoryReceipt()
    {
    }

    public int PurchaseId { get; private set; }

    /// <summary>The name the file is stored under (generated, never the uploaded name).</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>The name of the file on the admin's device, for display only.</summary>
    public string? OriginalFileName { get; private set; }

    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTime UploadedAtUtc { get; private set; }

    internal static InventoryReceipt Create(string fileName, string? originalFileName, string contentType, long sizeBytes, DateTime nowUtc)
    {
        // Some browsers send the full path (C:\Users\...\receipt.jpg); keep only the name, whatever the server's OS.
        var original = originalFileName?.Trim() ?? string.Empty;
        original = original[(original.LastIndexOfAny(['/', '\\']) + 1)..];
        return new InventoryReceipt
        {
            FileName = Guard.NotEmpty(fileName, "File name", 100),
            OriginalFileName = original.Length == 0 ? null : original.Length > 255 ? original[..255] : original,
            ContentType = Guard.NotEmpty(contentType, "Content type", 100),
            SizeBytes = sizeBytes,
            UploadedAtUtc = nowUtc,
        };
    }

    public void AttachTo(int purchaseId) => PurchaseId = purchaseId;
}
