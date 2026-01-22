namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for cached processed cat/dog images.
/// Stores ML-processed images to persist across app restarts.
/// </summary>
public class CachedImageEntity
{
    /// <summary>Auto-incrementing primary key.</summary>
    public int Id { get; set; }

    /// <summary>Image type: "cat" or "dog".</summary>
    public string ImageType { get; set; } = string.Empty;

    /// <summary>Processed JPEG image bytes (typically 15-30KB).</summary>
    public byte[] ImageData { get; set; } = Array.Empty<byte>();

    /// <summary>When this image was processed and cached.</summary>
    public DateTime CreatedAt { get; set; }
}
