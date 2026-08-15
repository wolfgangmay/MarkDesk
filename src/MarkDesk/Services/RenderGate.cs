using System.Threading;

namespace MarkDesk.Services;

/// <summary>
/// Version stamp for async rendering: each request claims a new version and
/// only the latest version may publish its result to the UI. Guarantees an
/// older (slower) render can never overwrite a newer one, even if the newer
/// one finished first.
/// </summary>
public sealed class RenderGate
{
    private long _version;

    public long Next() => Interlocked.Increment(ref _version);

    public bool TryClaim(long version) => Volatile.Read(ref _version) == version;
}