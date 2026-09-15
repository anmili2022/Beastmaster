namespace Beastmaster;

public sealed record BeastmasterChallengeNoteEntry(
    uint RowId,
    string Name,
    string Description,
    int RequiredAmount);

public static class BeastmasterChallengeNote
{
    private static IReadOnlyList<BeastmasterChallengeNoteEntry>? cachedEntries;

    public static IReadOnlyList<BeastmasterChallengeNoteEntry> GetBeastArenaEntries()
    {
        if (cachedEntries != null)
        {
            return cachedEntries;
        }

        var sheet = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentsNote>();
        cachedEntries = sheet
            .Where(row => row.RowId != 0
                && (row.Name.ExtractText().Contains("斗兽", StringComparison.Ordinal)
                    || row.Name.ExtractText().Contains("奇弈", StringComparison.Ordinal)
                    || row.Description.ExtractText().Contains("斗兽奇弈", StringComparison.Ordinal)))
            .OrderBy(row => row.RowId)
            .Select(row => new BeastmasterChallengeNoteEntry(
                row.RowId,
                row.Name.ExtractText(),
                row.Description.ExtractText(),
                row.RequiredAmount))
            .ToList();
        return cachedEntries;
    }

    public static unsafe bool IsComplete(uint rowId)
    {
        var note = FFXIVClientStructs.FFXIV.Client.Game.UI.ContentsNote.Instance();
        return note != null && note->IsContentNoteComplete((int)rowId);
    }

    public static unsafe bool IsLoaded()
    {
        var note = FFXIVClientStructs.FFXIV.Client.Game.UI.ContentsNote.Instance();
        return note != null && (int)note->State == 2;
    }
}
