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

/// <summary>PDF page margins in millimetres (user-configurable, default 18mm).</summary>
public readonly record struct PdfMargins(int TopMm, int BottomMm, int LeftMm, int RightMm)
{
    public const int MinMm = 5;
    public const int MaxMm = 40;
    public const int DefaultMm = 18;

    public static PdfMargins Default => new(DefaultMm, DefaultMm, DefaultMm, DefaultMm);

    public PdfMargins Clamped() => new(
        Math.Clamp(TopMm, MinMm, MaxMm),
        Math.Clamp(BottomMm, MinMm, MaxMm),
        Math.Clamp(LeftMm, MinMm, MaxMm),
        Math.Clamp(RightMm, MinMm, MaxMm));

    public static double MmToInches(int mm) => mm / 25.4;
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public sealed class AppSettings
{
    public int LayoutThresholdPx { get; set; } = 960;
    public ViewMode DefaultViewMode { get; set; } = ViewMode.Preview;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public string AssetsFolderName { get; set; } = "assets";
    public string ImageNamePattern { get; set; } = "img-{yyyyMMdd-HHmmss}-{n}";
    public bool ScrollSync { get; set; } = true;
    public int RenderDebounceMs { get; set; } = 150;
    public PdfPageSize PdfPageSize { get; set; } = PdfPageSize.A4;
    public int PdfMarginTopMm { get; set; } = PdfMargins.DefaultMm;
    public int PdfMarginBottomMm { get; set; } = PdfMargins.DefaultMm;
    public int PdfMarginLeftMm { get; set; } = PdfMargins.DefaultMm;
    public int PdfMarginRightMm { get; set; } = PdfMargins.DefaultMm;
    public int EditorFontSize { get; set; } = 14;
    public bool TypingAssists { get; set; } = true;
    public bool OutlineVisible { get; set; } = true;
    public int OutlineWidthPx { get; set; } = 220;
    public bool FilesPanelVisible { get; set; } = true;
    public int FilesPanelWidthPx { get; set; } = 240;
    public List<string> RecentFiles { get; set; } = new();

    public PdfMargins PdfMargins => new PdfMargins(PdfMarginTopMm, PdfMarginBottomMm, PdfMarginLeftMm, PdfMarginRightMm).Clamped();
}
