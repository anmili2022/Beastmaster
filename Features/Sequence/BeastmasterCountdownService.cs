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

    private bool customCountdownActive;
    private float customCountdownDuration;
    private float customCountdownRemaining;
    private DateTime customCountdownStartUtc;
    private const float CustomCountdownTickInterval = 50f;

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

    public bool IsCustomCountdownActive => customCountdownActive;

    public void Dispose()
        => DalamudApi.Framework.Update -= OnFrameworkUpdate;

    public void StartCustomCountdown(float seconds)
    {
        if (seconds <= 0f || seconds > 3600f)
        {
            DalamudApi.ChatGui.Print("[驯兽师助手] 倒计时秒数必须在 1~3600 之间。");
            return;
        }

        if (!configuration.AutoCaptureEnabled)
        {
            DalamudApi.ChatGui.Print("[驯兽师助手] 请先开启自动输出（/驯兽师 输出）。");
            return;
        }

        if (DalamudApi.PlayerState.ClassJob.RowId != BeastmasterClassJobId)
        {
            DalamudApi.ChatGui.Print("[驯兽师助手] 当前职业不是驯兽师。");
            return;
        }

        customCountdownActive = true;
        customCountdownDuration = seconds;
        customCountdownRemaining = seconds;
        customCountdownStartUtc = DateTime.UtcNow;

        LastTransition = BeastmasterCountdownTransition.Started;
        LastTransitionUtc = DateTime.UtcNow;

        var snapshot = new BeastmasterCountdownSnapshot(true, true, seconds, 0, $"自定义倒计时 {seconds:0.#} 秒");
        Snapshot = snapshot;

        DalamudApi.ChatGui.Print($"[驯兽师助手] 自定义倒计时 {seconds:0.#} 秒已启动，序列将开始执行。");
    }

    public void CancelCustomCountdown()
    {
        if (!customCountdownActive)
        {
            return;
        }

        customCountdownActive = false;
        customCountdownRemaining = 0f;

        LastTransition = BeastmasterCountdownTransition.Cancelled;
        LastTransitionUtc = DateTime.UtcNow;

        Snapshot = new(false, false, 0f, 0, "自定义倒计时已取消");
        DalamudApi.ChatGui.Print("[驯兽师助手] 自定义倒计时已取消。");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        _ = framework;
        if (!configuration.AutoCaptureEnabled
            || configuration.AutoOutputPaused
            || !DalamudApi.ClientState.IsLoggedIn
            || DalamudApi.PlayerState.ClassJob.RowId != BeastmasterClassJobId)
        {
            if (customCountdownActive)
            {
                CancelCustomCountdown();
            }

            Snapshot = DisabledSnapshot;
            LastPolledUtc = DateTime.MinValue;
            nextPollUtc = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;

        if (customCountdownActive)
        {
            var elapsed = (float)(now - customCountdownStartUtc).TotalSeconds;
            customCountdownRemaining = Math.Max(0f, customCountdownDuration - elapsed);

            if (customCountdownRemaining <= 0f)
            {
                customCountdownActive = false;
                LastTransition = BeastmasterCountdownTransition.Completed;
                LastTransitionUtc = now;
                Snapshot = new(true, false, 0f, 0, "自定义倒计时完成");
                DalamudApi.ChatGui.Print("[驯兽师助手] 自定义倒计时完成，等待进入战斗。");
            }
            else
            {
                Snapshot = new(true, true, customCountdownRemaining, 0, $"自定义倒计时 {customCountdownRemaining:0.#} 秒");
            }

            LastPolledUtc = now;
            nextPollUtc = now.AddMilliseconds(CustomCountdownTickInterval);
            return;
        }

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
