namespace MarkDesk.Services;

/// <summary>
/// Large-document tiers, calibrated against measured Markdig throughput
/// (see req.md §9.1): ≤1 MB renders in ~0.9 s (realtime-friendly), 1–5 MB
/// renders in 1–7 s (background render + longer debounce), >5 MB is not
/// renderable in reasonable time or memory (preview disabled).
/// </summary>
public enum DocumentTier
{
    /// <summary>≤ 1 MB — realtime full-featured preview.</summary>
    RealTime,

    /// <summary>1–5 MB — background render, longer debounce, "Rendering…".</summary>
    Medium,

    /// <summary>&gt; 5 MB — preview disabled, fast outline scan, read-only.</summary>
    Large
}

public static class DocumentTierResolver
{
    public const long RealTimeThresholdBytes = 1L * 1024 * 1024;
    public const long LargeThresholdBytes = 5L * 1024 * 1024;

    /// <summary>PDF export is refused above this (measured: 20 MB renders 120 s / +294 MB).</summary>
    public const long PdfExportLimitBytes = 20L * 1024 * 1024;

    public static DocumentTier ForBytes(long bytes) =>
        bytes <= RealTimeThresholdBytes ? DocumentTier.RealTime
        : bytes <= LargeThresholdBytes ? DocumentTier.Medium
        : DocumentTier.Large;
}