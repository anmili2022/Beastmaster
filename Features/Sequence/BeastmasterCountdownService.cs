using Dalamud.Plugin.Services;

namespace Beastmaster;

public enum BeastmasterCountdownTransition
{
    None,
    Started,
    Cancelled,
    Completed,
}

public sealed class BeastmasterCountdownService : IDisposable
{
    private const uint BeastmasterClassJobId = 43;
    private static readonly BeastmasterCountdownSnapshot DisabledSnapshot
        = new(false, false, 0f, 0, "自动输出未运行，未轮询倒计时");
    private readonly BeastmasterConfiguration configuration;
    private DateTime nextPollUtc = DateTime.MinValue;

    public BeastmasterCountdownService(BeastmasterConfiguration configuration)
    {
        this.configuration = configuration;
        DalamudApi.Framework.Update += OnFrameworkUpdate;
    }

    public BeastmasterCountdownSnapshot Snapshot { get; private set; }
        = new(false, false, 0f, 0, "等待轮询");

    public DateTime LastPolledUtc { get; private set; } = DateTime.MinValue;

    public BeastmasterCountdownTransition LastTransition { get; private set; }

    public DateTime LastTransitionUtc { get; private set; } = DateTime.MinValue;

    public void Dispose()
        => DalamudApi.Framework.Update -= OnFrameworkUpdate;

    private void OnFrameworkUpdate(IFramework framework)
    {
        _ = framework;
        if (!configuration.AutoCaptureEnabled
            || configuration.AutoOutputPaused
            || !DalamudApi.ClientState.IsLoggedIn
            || DalamudApi.PlayerState.ClassJob.RowId != BeastmasterClassJobId)
        {
            Snapshot = DisabledSnapshot;
            LastPolledUtc = DateTime.MinValue;
            nextPollUtc = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;
        if (now < nextPollUtc)
        {
            return;
        }

        var previous = Snapshot;
        var current = BeastmasterCountdownSnapshot.Read();
        if (current.Available && !previous.Active && current.Active)
        {
            LastTransition = BeastmasterCountdownTransition.Started;
            LastTransitionUtc = now;
        }
        else if (previous.Active && current.Available && !current.Active)
        {
            LastTransition = previous.TimeRemaining > 0.25f
                ? BeastmasterCountdownTransition.Cancelled
                : BeastmasterCountdownTransition.Completed;
            LastTransitionUtc = now;
        }

        Snapshot = current;
        LastPolledUtc = now;
        nextPollUtc = now.AddMilliseconds(Snapshot.Active ? 50 : 100);
    }
}
