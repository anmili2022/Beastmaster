using Lumina.Excel.Sheets;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Beastmaster;

public sealed class BeastmasterDebugDataService
{
    private const int ResultLimit = 200;

    public string GetCharacter()
    {
        var contentId = DalamudApi.PlayerState.ContentId;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (!DalamudApi.ClientState.IsLoggedIn || player == null)
        {
            return "未登录，无法读取角色资料。";
        }

        var world = player.HomeWorld.Value.Name.ExtractText();
        return JoinLines(
            "类型: 当前角色",
            $"ContentId: {contentId.ToString(CultureInfo.InvariantCulture)}",
            $"名称: {player.Name.TextValue}",
            $"服务器: {world}");
    }

    public string GetLocation()
    {
        var territoryType = DalamudApi.ClientState.TerritoryType;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        var position = player?.Position ?? Vector3.Zero;
        var territoryName = string.Empty;
        uint mapRowId = 0;

        if (DalamudApi.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryType, out var territory))
        {
            territoryName = territory.PlaceName.Value.Name.ExtractText();
            mapRowId = territory.Map.RowId;
        }

        return JoinLines(
            "类型: 当前位置",
            $"TerritoryType: {territoryType}",
            $"区域: {territoryName}",
            $"Map.RowId: {mapRowId}",
            player == null
                ? "坐标: 无本地角色"
                : $"世界坐标: X={position.X:0.###}, Y={position.Y:0.###}, Z={position.Z:0.###}");
    }

    public string FindClassJobs(string query)
        => FormatMatches(
            "职业 ClassJob",
            query,
            DalamudApi.DataManager.GetExcelSheet<ClassJob>()
                .Select(row => (row.RowId, Name: row.Name.ExtractText())));

    public string FindQuests(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return "任务 Quest\n请输入名称关键词后再读取。";
        }

        var matches = DalamudApi.DataManager.GetExcelSheet<Quest>()
            .Where(row => row.RowId != 0
                && row.Name.ExtractText().Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.RowId)
            .Take(ResultLimit + 1)
            .ToArray();
        var builder = new StringBuilder()
            .AppendLine("类型: 任务 Quest")
            .AppendLine($"关键词: {query}");

        foreach (var quest in matches.Take(ResultLimit))
        {
            var name = quest.Name.ExtractText();
            builder.AppendLine($"RowId={quest.RowId} | {name}");
            builder.AppendLine($"  IssuerStart={quest.IssuerStart.RowId} | IssuerLocation={quest.IssuerLocation.RowId}");
            builder.AppendLine($"  JournalGenre={quest.JournalGenre.RowId} | ClassJobCategory={quest.ClassJobCategory0.RowId}");
            builder.AppendLine($"  PreviousQuest={string.Join(',', quest.PreviousQuest.Select(previous => previous.RowId).Where(rowId => rowId != 0))}");

            if (quest.IssuerLocation.RowId == 0)
            {
                continue;
            }

            var level = quest.IssuerLocation.Value;
            var npcName = DalamudApi.DataManager.GetExcelSheet<ENpcResident>()
                .TryGetRow(quest.IssuerStart.RowId, out var npc)
                    ? npc.Singular.ExtractText()
                    : string.Empty;
            var zone = level.Territory.Value.PlaceName.Value.Name.ExtractText();
            builder.AppendLine($"  开始NPC={npcName} | TerritoryType={level.Territory.RowId} | Map.RowId={level.Map.RowId}");
            builder.AppendLine($"  世界坐标: X={level.X:0.###}, Y={level.Y:0.###}, Z={level.Z:0.###} | 区域={zone}");
        }

        if (matches.Length == 0)
        {
            builder.AppendLine("未找到匹配内容。");
        }
        else if (matches.Length > ResultLimit)
        {
            builder.AppendLine($"结果超过 {ResultLimit} 条，请使用更具体的关键词。");
        }

        return builder.ToString().TrimEnd();
    }

    public string FindBeastmasterQuestChain()
    {
        const uint beastmasterJournalGenre = 198;
        const uint beastmasterClassJobCategory = 203;
        var matches = DalamudApi.DataManager.GetExcelSheet<Quest>()
            .Where(quest => quest.RowId != 0
                && (quest.JournalGenre.RowId == beastmasterJournalGenre
                    || quest.ClassJobCategory0.RowId == beastmasterClassJobCategory))
            .OrderBy(quest => quest.RowId)
            .ToArray();
        var builder = new StringBuilder()
            .AppendLine("类型: 驯兽师任务链候选")
            .AppendLine($"条件: JournalGenre={beastmasterJournalGenre} 或 ClassJobCategory={beastmasterClassJobCategory}");

        foreach (var quest in matches)
        {
            builder.AppendLine($"RowId={quest.RowId} | {quest.Name.ExtractText()}");
            builder.AppendLine($"  JournalGenre={quest.JournalGenre.RowId} | ClassJobCategory={quest.ClassJobCategory0.RowId}");
            builder.AppendLine($"  PreviousQuest={string.Join(',', quest.PreviousQuest.Select(previous => previous.RowId).Where(rowId => rowId != 0))}");
        }

        if (matches.Length == 0)
        {
            builder.AppendLine("未找到匹配内容。");
        }

        return builder.ToString().TrimEnd();
    }

    public string FindItems(string query)
        => FormatMatches(
            "物品 Item",
            query,
            DalamudApi.DataManager.GetExcelSheet<Item>()
                .Select(row => (row.RowId, Name: row.Name.ExtractText())));

    public string FindNpcs(string query)
        => FormatMatches(
            "NPC ENpcResident",
            query,
            DalamudApi.DataManager.GetExcelSheet<ENpcResident>()
                .Select(row => (row.RowId, Name: row.Singular.ExtractText())));

    public string FindMonsters(string query)
        => FormatMatches(
            "怪物 BNpcName",
            query,
            DalamudApi.DataManager.GetExcelSheet<BNpcName>()
                .Select(row => (row.RowId, Name: row.Singular.ExtractText())));

    public string FindDuties(string query)
        => FormatMatches(
            "副本 ContentFinderCondition",
            query,
            DalamudApi.DataManager.GetExcelSheet<ContentFinderCondition>()
                .Select(row => (row.RowId, Name: row.Name.ExtractText())));

    private static string FormatMatches(
        string category,
        string query,
        IEnumerable<(uint RowId, string Name)> rows)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return $"{category}\n请输入名称关键词后再读取。";
        }

        var matches = rows
            .Where(row => row.RowId != 0
                && !string.IsNullOrWhiteSpace(row.Name)
                && row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.RowId)
            .Take(ResultLimit + 1)
            .ToArray();
        var truncated = matches.Length > ResultLimit;

        var builder = new StringBuilder()
            .AppendLine($"类型: {category}")
            .AppendLine($"关键词: {query}");
        foreach (var match in matches.Take(ResultLimit))
        {
            builder.Append("RowId=")
                .Append(match.RowId)
                .Append(" | ")
                .AppendLine(match.Name);
        }

        if (matches.Length == 0)
        {
            builder.AppendLine("未找到匹配内容。");
        }
        else if (truncated)
        {
            builder.AppendLine($"结果超过 {ResultLimit} 条，请使用更具体的关键词。");
        }

        return builder.ToString().TrimEnd();
    }

    private static string JoinLines(params string[] lines)
        => string.Join(Environment.NewLine, lines);
}
