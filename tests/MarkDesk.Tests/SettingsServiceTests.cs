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
        Assert.Equal(ViewMode.Split, settings.DefaultViewMode);
        Assert.Equal("assets", settings.AssetsFolderName);
        Assert.Equal("img-{yyyyMMdd-HHmmss}-{n}", settings.ImageNamePattern);
        Assert.True(settings.ScrollSync);
        Assert.Equal(150, settings.RenderDebounceMs);
        Assert.Equal(PdfPageSize.A4, settings.PdfPageSize);
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
            Assert.Equal(ViewMode.Split, service.Current.DefaultViewMode);
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
            first.Save();

            var reloaded = new SettingsService(dir);
            Assert.Equal(1200, reloaded.Current.LayoutThresholdPx);
            Assert.Equal(ViewMode.Edit, reloaded.Current.DefaultViewMode);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
