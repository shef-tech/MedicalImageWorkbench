namespace Workbench.Core;

public class ImageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OriginalFileName { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string FileFormat { get; set; } = "";
    public string CollectionName { get; set; } = "";

    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}