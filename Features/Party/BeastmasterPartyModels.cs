namespace Beastmaster;

[Serializable]
public sealed class BeastmasterPartyPreset
{
    private const string FormatHeader = "BSTPARTY|1";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "10栏位编队";
    public int SlotCount { get; set; } = 10;
    public List<int> Members { get; set; } = [];

    public BeastmasterPartyPreset Clone()
        => new()
        {
            Name = Name + " 副本",
            SlotCount = SlotCount,
            Members = [.. Members],
        };

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "预设名称不能为空。";
            return false;
        }

        if (SlotCount is not (10 or 12 or 14 or 15))
        {
            error = "预设栏位数量只能是 10、12、14 或 15。";
            return false;
        }

        if (Members.Count > SlotCount)
        {
            error = $"当前预设最多配置 {SlotCount} 只魔兽。";
            return false;
        }

        if (Members.Any(number => number is < 1 or > 50))
        {
            error = "魔兽图鉴编号必须在 1~50 之间。";
            return false;
        }

        if (Members.Distinct().Count() != Members.Count)
        {
            error = "同一只魔兽不能在预设中重复出现。";
            return false;
        }

        return true;
    }

    public string Export()
        => string.Join(Environment.NewLine,
            FormatHeader,
            $"名称|{Name.Replace('\r', ' ').Replace('\n', ' ').Replace('|', ' ')}",
            $"栏位|{SlotCount}",
            $"成员|{string.Join(',', Members)}");

    public static bool TryImport(string text, out BeastmasterPartyPreset? preset, out string error)
    {
        preset = null;
        error = "编队预设内容无效。";
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        {
            return false;
        }

        var lines = text.Replace("\r", string.Empty).Split('\n');
        if (lines.Length < 4 || lines[0].Trim() != FormatHeader)
        {
            error = $"第一行必须是 {FormatHeader}。";
            return false;
        }

        var result = new BeastmasterPartyPreset();
        foreach (var line in lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var separator = line.IndexOf('|');
            if (separator <= 0)
            {
                return false;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];
            switch (key)
            {
                case "名称": result.Name = value; break;
                case "栏位" when int.TryParse(value, out var slotCount): result.SlotCount = slotCount; break;
                case "成员":
                    result.Members.Clear();
                    if (value.Length > 0)
                    {
                        foreach (var member in value.Split(','))
                        {
                            if (!int.TryParse(member, out var number)) return false;
                            result.Members.Add(number);
                        }
                    }
                    break;
                default: return false;
            }
        }

        if (!result.TryValidate(out error)) return false;
        result.Id = Guid.NewGuid().ToString("N");
        preset = result;
        return true;
    }
}

public sealed record BeastmasterPetPartyMember(int Position, int CatalogNumber, string Name);

public sealed record BeastmasterPetPartySnapshot(
    bool Available,
    int MemberCount,
    int Capacity,
    IReadOnlyList<BeastmasterPetPartyMember> Members,
    string Reason)
{
    public static readonly int[] SupportedCapacities = [10, 12, 14, 15];

    public static BeastmasterPetPartySnapshot Unavailable(string reason)
        => new(false, 0, 0, [], reason);
}
