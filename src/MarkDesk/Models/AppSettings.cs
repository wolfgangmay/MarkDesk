namespace MarkDesk.Models;

public enum ViewMode
{
    Edit,
    Split,
    Preview
}

public enum PdfPageSize
{
    A4,
    Letter
}

public sealed class AppSettings
{
    public int LayoutThresholdPx { get; set; } = 960;
    public ViewMode DefaultViewMode { get; set; } = ViewMode.Split;
    public string AssetsFolderName { get; set; } = "assets";
    public string ImageNamePattern { get; set; } = "img-{yyyyMMdd-HHmmss}-{n}";
    public bool ScrollSync { get; set; } = true;
    public int RenderDebounceMs { get; set; } = 150;
    public PdfPageSize PdfPageSize { get; set; } = PdfPageSize.A4;
}
