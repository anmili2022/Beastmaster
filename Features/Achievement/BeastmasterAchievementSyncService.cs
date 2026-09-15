using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Beastmaster;

public sealed unsafe class BeastmasterAchievementSyncService
{
    private const long TimeoutMs = 15000;
    private readonly BeastmasterProgressService progressService;
    private bool scanning;
    private long scanStartedAt;
    private long nextActionAt;
    private string status = "尚未同步。";

    public BeastmasterAchievementSyncService(BeastmasterProgressService progressService)
    {
        this.progressService = progressService;
    }

    public string Status => status;
    public bool IsScanning => scanning;
    public string Diagnostic { get; private set; } = string.Empty;

    public void RequestSync()
    {
        if (progressService.CurrentCharacterKey.Length == 0)
        {
            status = "请先登录角色。";
            return;
        }

        scanning = true;
        scanStartedAt = Environment.TickCount64;
        nextActionAt = 0;
        Diagnostic = string.Empty;
        status = "正在读取当前角色的成就完成情况…";
    }

    public void Update()
    {
        if (!scanning)
        {
            return;
        }

        try
        {
            if (progressService.CurrentCharacterKey.Length == 0)
            {
                Stop("角色已登出，同步已取消。");
                return;
            }

            if (Environment.TickCount64 - scanStartedAt >= TimeoutMs)
            {
                throw new InvalidOperationException("同步超时：成就数据未能及时加载。");
            }

            var achievement = Achievement.Instance();
            if (achievement == null)
            {
                throw new InvalidOperationException("成就系统不可用。");
            }

            if (!achievement->IsLoaded())
            {
                if (Environment.TickCount64 >= nextActionAt)
                {
                    achievement->RequestCompletedAchievements();
                    nextActionAt = Environment.TickCount64 + 500;
                }

                status = "等待成就数据加载…";
                return;
            }

            var completedIds = new HashSet<int>();
            foreach (var group in BeastmasterAchievementCatalog.Groups)
            {
                foreach (var achievementId in group.AchievementIds)
                {
                    if (achievement->IsComplete(achievementId))
                    {
                        completedIds.Add(achievementId);
                    }
                }
            }

            var changed = progressService.ReplaceAchievementProgress(completedIds);
            Stop($"同步完成：已完成 {completedIds.Count}/{BeastmasterAchievementCatalog.AchievementCount}，更新 {changed} 项。");
        }
        catch (Exception ex)
        {
            Diagnostic = ex.Message;
            Stop($"同步失败，未修改进度：{ex.Message}");
        }
    }

    public void Start()
    {
        DalamudApi.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Update();
    }

    private void Stop(string message)
    {
        scanning = false;
        scanStartedAt = 0;
        status = message;
    }
}
