namespace Workbench.Core;

public class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ImageId { get; set; }

    public string Label { get; set; } = "";
    public string Notes { get; set; } = "";

    // Coordinates measured in original-image pixels.
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}