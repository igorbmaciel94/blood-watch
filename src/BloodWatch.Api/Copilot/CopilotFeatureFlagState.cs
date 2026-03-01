using System.Threading;
using BloodWatch.Api.Options;
using Microsoft.Extensions.Options;

namespace BloodWatch.Api.Copilot;

public sealed class CopilotFeatureFlagState(IOptions<CopilotOptions> options) : ICopilotFeatureFlagState
{
    private int _enabled = options.Value.Enabled ? 1 : 0;
    private long _updatedAtUtcTicks = DateTime.UtcNow.Ticks;

    public bool IsEnabled => Volatile.Read(ref _enabled) == 1;

    public DateTime UpdatedAtUtc => new(Volatile.Read(ref _updatedAtUtcTicks), DateTimeKind.Utc);

    public void SetEnabled(bool enabled)
    {
        var desired = enabled ? 1 : 0;
        var previous = Interlocked.Exchange(ref _enabled, desired);

        if (previous != desired)
        {
            Volatile.Write(ref _updatedAtUtcTicks, DateTime.UtcNow.Ticks);
        }
    }
}
