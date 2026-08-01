using MarkDesk.Models;

namespace MarkDesk.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
}
