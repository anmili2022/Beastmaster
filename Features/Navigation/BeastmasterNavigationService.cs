using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Beastmaster;

public sealed class BeastmasterNavigationService : IDisposable
{
    private readonly BeastmasterConfiguration configuration;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICallGateSubscriber<bool> isReady;
    private readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveTo;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPoint;
    private readonly ICallGateSubscriber<object> stop;
    private readonly ICallGateSubscriber<uint, byte, bool> teleport;
    private BeastmasterQuestLocation? pendingLocation;
    private bool pendingLocationUseFieldNavigation;
    private BeastmasterQuestLocation? pendingVnavLocation;
    private bool pendingVnavFieldNavigation;
    private DateTime nextVnavCheckUtc = DateTime.MinValue;
    private DateTime pendingStartedUtc;
    private BeastmasterQuestLocation? pendingMountLocation;
    private DateTime pendingMountStartedUtc;
    private DateTime lastMountAttemptUtc = DateTime.MinValue;

    public BeastmasterNavigationService(
        IDalamudPluginInterface pluginInterface,
        BeastmasterConfiguration configuration)
    {
        this.pluginInterface = pluginInterface;
        this.configuration = configuration;
        isReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfindAndMoveTo = pluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        nearestPoint = pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint");
        stop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        teleport = pluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
        DalamudApi.Framework.Update += OnFrameworkUpdate;
    }

    public bool IsVnavmeshInstalled => IsPluginLoaded("vnavmesh");
    public bool IsLifestreamInstalled => IsPluginLoaded("Lifestream");

    public void Dispose()
    {
        DalamudApi.Framework.Update -= OnFrameworkUpdate;
        Stop();
    }

    public bool Navigate(BeastmasterQuestLocation location)
    {
        if (DalamudApi.ObjectTable.LocalPlayer == null)
        {
            return false;
        }

        if (!IsVnavmeshInstalled)
        {
            DalamudApi.ChatGui.Print("[驯兽师助手] vnavmesh 未加载，无法开始导航。");
            return false;
        }

        SetMapFlag(location);
        if (DalamudApi.ClientState.TerritoryType != location.TerritoryType)
        {
            return TeleportAndContinue(location, true);
        }

        return StartFieldNavigation(location);
    }

    public bool NavigateQuestTarget(BeastmasterQuestLocation location)
    {
        if (DalamudApi.ObjectTable.LocalPlayer == null || !IsVnavmeshInstalled)
        {
            if (!IsVnavmeshInstalled)
            {
                DalamudApi.ChatGui.Print("[驯兽师助手] vnavmesh 未加载，无法开始导航。");
            }

            return false;
        }

        SetMapFlag(location);
        return DalamudApi.ClientState.TerritoryType != location.TerritoryType
            ? TeleportAndContinue(location, true)
            : StartFieldNavigation(location);
    }

    public bool Navigate(BeastmasterCatalogEntry entry)
    {
        if (entry.LocationType != BeastmasterCatalogLocationType.Field
            || !entry.MapX.HasValue
            || !entry.MapY.HasValue)
        {
            return false;
        }

        var maps = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
        var map = maps
            .FirstOrDefault(candidate => candidate.TerritoryType.RowId != 0
                && candidate.SizeFactor > 0
                && (entry.TerritoryType == 0 || candidate.TerritoryType.RowId == entry.TerritoryType)
                && (entry.MapRowId == 0 || candidate.RowId == entry.MapRowId)
                && candidate.PlaceName.Value.Name.ExtractText().Equals(entry.Location, StringComparison.Ordinal));

        // Some client map rows use a different territory/map pairing than the
        // catalog source. Fall back to the localized map name and retain the
        // catalog identifiers for navigation and map flags.
        if (map.RowId == 0)
        {
            map = maps.FirstOrDefault(candidate => candidate.TerritoryType.RowId != 0
                && candidate.SizeFactor > 0
                && candidate.PlaceName.Value.Name.ExtractText().Equals(entry.Location, StringComparison.Ordinal));
        }

        if (map.RowId == 0 && !(entry.WorldX.HasValue && entry.WorldY.HasValue && entry.WorldZ.HasValue))
        {
            DalamudApi.ChatGui.Print($"[驯兽师助手] 客户端地图表中找不到 {entry.Location}。");
            return false;
        }

        var position = entry.WorldX.HasValue && entry.WorldY.HasValue && entry.WorldZ.HasValue
            ? new Vector3(entry.WorldX.Value, entry.WorldY.Value, entry.WorldZ.Value)
            : new Vector3(
                50f * entry.MapX!.Value - map.OffsetX - 102400f / map.SizeFactor - 50f,
                0f,
                50f * entry.MapY!.Value - map.OffsetY - 102400f / map.SizeFactor - 50f);
        var location = new BeastmasterQuestLocation(
            entry.TerritoryType != 0 ? entry.TerritoryType : map.TerritoryType.RowId,
            entry.MapRowId != 0 ? entry.MapRowId : map.RowId,
            position,
            entry.Location,
            entry.Name);

        SetMapFlag(location);
        if (DalamudApi.ClientState.TerritoryType != location.TerritoryType)
        {
            return TeleportAndContinue(location, true);
        }

        return StartFieldNavigation(location);
    }

    public unsafe bool OpenDutyFinder(BeastmasterCatalogEntry entry)
    {
        if (entry.LocationType != BeastmasterCatalogLocationType.Duty)
        {
            return false;
        }

        var duty = entry.ContentFinderConditionId != 0
            ? DalamudApi.DataManager.GetExcelSheet<ContentFinderCondition>()
                .FirstOrDefault(candidate => candidate.RowId == entry.ContentFinderConditionId)
            : default;
        if (duty.RowId == 0)
        {
            var normalizedDutyName = NormalizeDutyName(entry.Location);
            duty = DalamudApi.DataManager.GetExcelSheet<ContentFinderCondition>()
                .FirstOrDefault(candidate => candidate.TerritoryType.RowId != 0
                    && NormalizeDutyName(candidate.Name.ExtractText()).Equals(normalizedDutyName, StringComparison.Ordinal));
        }
        if (duty.RowId == 0)
        {
            DalamudApi.ChatGui.Print($"[驯兽师助手] 任务搜索器中找不到“{entry.Location}”。");
            return false;
        }

        try
        {
            AgentContentsFinder.Instance()->OpenRegularDuty(duty.RowId);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Warning(ex, "Failed to open Duty Finder for {DutyName}.", entry.Location);
            DalamudApi.ChatGui.Print($"[驯兽师助手] 打开任务搜索器失败：{ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        pendingLocation = null;
        pendingLocationUseFieldNavigation = false;
        pendingVnavLocation = null;
        pendingMountLocation = null;
        try
        {
            stop.InvokeAction();
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Warning(ex, "Failed to stop vnavmesh navigation.");
        }
    }

    private bool StartFieldNavigation(BeastmasterQuestLocation location)
    {
        if (!IsVnavmeshInstalled || DalamudApi.ObjectTable.LocalPlayer == null)
        {
            return false;
        }

        try
        {
            if (!isReady.InvokeFunc())
            {
                pendingVnavLocation = location;
                pendingVnavFieldNavigation = true;
                DalamudApi.ChatGui.Print("[驯兽师助手] vnavmesh 未就绪，准备完成后将自动开始导航。 ");
                return true;
            }

            var player = DalamudApi.ObjectTable.LocalPlayer;
            var targetWithHeight = player == null ? location.Position : location.Position with { Y = player.Position.Y };
            var target = nearestPoint.InvokeFunc(targetWithHeight, 120f, 300f) ?? targetWithHeight;
            var fly = configuration.UseFlightNavigation && IsFlightUnlocked(location.TerritoryType);
            if (fly && !DalamudApi.Condition[ConditionFlag.Mounted])
            {
                pendingMountLocation = location with { Position = target };
                pendingMountStartedUtc = DateTime.UtcNow;
                lastMountAttemptUtc = DateTime.MinValue;
                if (configuration.ShowNavigationLogs)
                {
                    DalamudApi.ChatGui.Print("[驯兽师助手] 正在上坐骑，成功后继续飞行导航。 ");
                }

                return true;
            }

            var started = pathfindAndMoveTo.InvokeFunc(target, fly);
            if (configuration.ShowNavigationLogs)
            {
                DalamudApi.ChatGui.Print(started
                    ? $"[驯兽师助手] 开始{(fly ? "飞行" : "步行")}导航到 {location.Zone} {location.NpcName}。"
                    : $"[驯兽师助手] vnavmesh 未能开始前往 {location.NpcName}。 ");
            }

            return started;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Warning(ex, "Failed to navigate to Beastmaster catalog location.");
            DalamudApi.ChatGui.Print($"[驯兽师助手] 导航失败：{ex.Message}");
            return false;
        }
    }

    private bool TeleportAndContinue(BeastmasterQuestLocation location, bool useFieldNavigation)
    {
        if (!IsLifestreamInstalled)
        {
            DalamudApi.ChatGui.Print($"[驯兽师助手] 目标位于 {location.Zone}，Lifestream 未加载，请手动前往。 ");
            return false;
        }

        var aetheryteId = DalamudApi.DataManager.GetExcelSheet<Aetheryte>()
            .FirstOrDefault(aetheryte => aetheryte.IsAetheryte && aetheryte.Territory.RowId == location.TerritoryType)
            .RowId;
        if (aetheryteId == 0)
        {
            DalamudApi.ChatGui.Print($"[驯兽师助手] {location.Zone} 未找到可用以太水晶。 ");
            return false;
        }

        try
        {
            if (!teleport.InvokeFunc(aetheryteId, 0))
            {
                DalamudApi.ChatGui.Print("[驯兽师助手] Lifestream 未开始传送。 ");
                return false;
            }

            pendingLocation = location;
            pendingLocationUseFieldNavigation = useFieldNavigation;
            pendingStartedUtc = DateTime.UtcNow;
            DalamudApi.ChatGui.Print($"[驯兽师助手] 正在传送到 {location.Zone}，读图后将继续导航。 ");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Warning(ex, "Failed to teleport for Beastmaster catalog navigation.");
            DalamudApi.ChatGui.Print($"[驯兽师助手] Lifestream 调用失败：{ex.Message}");
            return false;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        _ = framework;
        ProcessPendingVnav();
        ProcessPendingMount();
        if (pendingLocation == null)
        {
            return;
        }

        if (DateTime.UtcNow - pendingStartedUtc > TimeSpan.FromSeconds(45))
        {
            pendingLocation = null;
            DalamudApi.ChatGui.Print("[驯兽师助手] 等待传送超时，已取消后续导航。 ");
            return;
        }

        if (DalamudApi.Condition[ConditionFlag.BetweenAreas])
        {
            return;
        }

        if (DateTime.UtcNow - pendingStartedUtc < TimeSpan.FromSeconds(4)
            || DalamudApi.ObjectTable.LocalPlayer == null)
        {
            return;
        }

        if (DalamudApi.ClientState.TerritoryType != pendingLocation.TerritoryType)
        {
            return;
        }

        var location = pendingLocation;
        var useFieldNavigation = pendingLocationUseFieldNavigation;
        pendingLocation = null;
        pendingLocationUseFieldNavigation = false;
        if (useFieldNavigation)
        {
            StartFieldNavigation(location);
        }
        else
        {
            Navigate(location);
        }
    }

    private void ProcessPendingVnav()
    {
        if (pendingVnavLocation == null || DalamudApi.ObjectTable.LocalPlayer == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now < nextVnavCheckUtc)
        {
            return;
        }

        nextVnavCheckUtc = now.AddMilliseconds(500);

        try
        {
            if (!isReady.InvokeFunc())
            {
                return;
            }
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Warning(ex, "Failed to check vnavmesh readiness.");
            return;
        }

        var location = pendingVnavLocation;
        var fieldNavigation = pendingVnavFieldNavigation;
        pendingVnavLocation = null;
        pendingVnavFieldNavigation = false;
        if (fieldNavigation)
        {
            StartFieldNavigation(location);
        }
        else
        {
            StartFieldNavigation(location);
        }
    }

    private unsafe void ProcessPendingMount()
    {
        if (pendingMountLocation == null)
        {
            return;
        }

        var location = pendingMountLocation;
        var now = DateTime.UtcNow;
        if (DalamudApi.Condition[ConditionFlag.Mounted])
        {
            pendingMountLocation = null;
            StartPathfind(location, true);
            return;
        }

        if (DalamudApi.Condition[ConditionFlag.InCombat]
            || now - pendingMountStartedUtc > TimeSpan.FromSeconds(8))
        {
            pendingMountLocation = null;
            if (configuration.ShowNavigationLogs)
            {
                DalamudApi.ChatGui.Print("[驯兽师助手] 无法上坐骑，改为步行导航。 ");
            }

            StartPathfind(location, false);
            return;
        }

        if (now - lastMountAttemptUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        lastMountAttemptUtc = now;
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
    }

    private bool StartPathfind(BeastmasterQuestLocation location, bool fly)
    {
        try
        {
            if (!isReady.InvokeFunc())
            {
                pendingVnavLocation = location;
                pendingVnavFieldNavigation = true;
                return true;
            }

            var started = pathfindAndMoveTo.InvokeFunc(location.Position, fly);
            if (configuration.ShowNavigationLogs)
            {
                DalamudApi.ChatGui.Print(started
                    ? $"[驯兽师助手] 开始{(fly ? "飞行" : "步行")}导航到 {location.Zone} {location.NpcName}。"
                    : $"[驯兽师助手] vnavmesh 未能开始前往 {location.NpcName}。 ");
            }

            return started;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Warning(ex, "Failed to start Beastmaster pathfinding.");
            DalamudApi.ChatGui.Print($"[驯兽师助手] 导航失败：{ex.Message}");
            return false;
        }
    }

    private static unsafe bool IsFlightUnlocked(uint territoryType)
    {
        if (!DalamudApi.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryType, out var territory)
            || territory.AetherCurrentCompFlgSet.RowId == 0)
        {
            return false;
        }

        var playerState = PlayerState.Instance();
        return playerState != null
            && playerState->IsAetherCurrentZoneComplete(territory.AetherCurrentCompFlgSet.RowId);
    }

    private bool IsPluginLoaded(string internalName)
        => pluginInterface.InstalledPlugins.Any(plugin =>
            plugin.IsLoaded && plugin.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeDutyName(string name)
        => new(name.Where(character => !char.IsWhiteSpace(character)
            && character is not '·' and not '：' and not ':').ToArray());

    private unsafe void SetMapFlag(BeastmasterQuestLocation location)
    {
        if (!configuration.SetFlagOnNavigation)
        {
            return;
        }

        try
        {
            AgentMap.Instance()->SetFlagMapMarker(location.TerritoryType, location.MapRowId, location.Position);
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Warning(ex, "Failed to set Beastmaster quest map flag.");
        }
    }
}
