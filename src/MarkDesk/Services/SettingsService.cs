using System.IO;
using System.Text.Json;
using MarkDesk.Models;

namespace MarkDesk.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _filePath;

    public SettingsService() : this(directory: null)
    {
    }

    public SettingsService(string? directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MarkDesk")
            : directory;
        _filePath = Path.Combine(_directory, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
