using System.IO;
using MarkDesk.Models;
using MarkDesk.Services;

namespace MarkDesk.Tests;

public class SettingsServiceTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "MarkDeskTest_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AppSettings_HasExpectedDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(960, settings.LayoutThresholdPx);
        Assert.Equal(ViewMode.Preview, settings.DefaultViewMode);
        Assert.Equal("assets", settings.AssetsFolderName);
        Assert.Equal("img-{yyyyMMdd-HHmmss}-{n}", settings.ImageNamePattern);
        Assert.True(settings.ScrollSync);
        Assert.Equal(150, settings.RenderDebounceMs);
        Assert.Equal(PdfPageSize.A4, settings.PdfPageSize);
        Assert.Equal(PdfMargins.Default, settings.PdfMargins);
    }

    [Fact]
    public void PdfMargins_ClampsToSupportedRange()
    {
        var tooSmall = new PdfMargins(0, 1, 2, 4).Clamped();
        Assert.Equal(new PdfMargins(5, 5, 5, 5), tooSmall);

        var tooLarge = new PdfMargins(41, 100, 40, 39).Clamped();
        Assert.Equal(new PdfMargins(40, 40, 40, 39), tooLarge);
    }

    [Fact]
    public void PdfMargins_ConvertsMmToInches()
    {
        Assert.Equal(18 / 25.4, PdfMargins.MmToInches(18), precision: 10);
        Assert.Equal(5.0, PdfMargins.MmToInches(127), precision: 10);
    }

    [Fact]
    public void LoadReturnsDefaultsWhenNoFile()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var service = new SettingsService(dir);

            Assert.Equal(960, service.Current.LayoutThresholdPx);
            Assert.Equal(ViewMode.Preview, service.Current.DefaultViewMode);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RoundTripsChangedValues()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var first = new SettingsService(dir);
            first.Current.LayoutThresholdPx = 1200;
            first.Current.DefaultViewMode = ViewMode.Edit;
            first.Current.PdfMarginTopMm = 12;
            first.Current.PdfMarginBottomMm = 14;
            first.Current.PdfMarginLeftMm = 16;
            first.Current.PdfMarginRightMm = 10;
            first.Save();

            var reloaded = new SettingsService(dir);
            Assert.Equal(1200, reloaded.Current.LayoutThresholdPx);
            Assert.Equal(ViewMode.Edit, reloaded.Current.DefaultViewMode);
            Assert.Equal(new PdfMargins(12, 14, 16, 10), reloaded.Current.PdfMargins);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
