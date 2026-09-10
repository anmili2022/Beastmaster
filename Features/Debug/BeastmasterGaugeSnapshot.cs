using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

namespace Beastmaster;

public sealed class BeastmasterGaugeSnapshot
{
    public const int MaximumGauge = 250;
    public const int ComboGaugeRequirement = 100;
    private const uint BeastmasterClassJobId = 43;
    private const int RawGaugeLength = 48;
    private const int BeastPowerOffset = 17;
    private const int WhistleIndexOffset = 19;
    private const int BeastHeartOffset = 24;
    private const int TpOffset = 16;
    private const uint WhiteStatusId = 4599;
    private const uint PurpleStatusId = 4600;

    private BeastmasterGaugeSnapshot(
        bool available,
        string status,
        byte[] bytes,
        nint address,
        uint summonDataId = 0,
        string summonName = "",
        bool hasWhiteStatus = false,
        bool hasPurpleStatus = false)
    {
        Available = available;
        Status = status;
        Bytes = bytes;
        Address = address;
        SummonDataId = summonDataId;
        SummonName = summonName;
        HasWhiteStatus = hasWhiteStatus;
        HasPurpleStatus = hasPurpleStatus;
    }

    public bool Available { get; }

    public string Status { get; }

    public byte[] Bytes { get; }

    public nint Address { get; }

    public uint SummonDataId { get; }

    public string SummonName { get; }

    public bool HasWhiteStatus { get; }

    public bool HasPurpleStatus { get; }

    public byte Tp => GetByte(TpOffset);

    public byte BeastPower => GetByte(BeastPowerOffset);

    public byte WhistleIndex => GetByte(WhistleIndexOffset);

    public byte BeastHeart => GetByte(BeastHeartOffset);

    public int BeastHeartStacks => BeastHeart >> 2;

    public int BeastSoulStacks => BeastHeart & 3;

    public BeastmasterCatalogEntry? SummonEntry
        => SummonDataId == 0
            ? null
            : BeastmasterCatalog.Entries.FirstOrDefault(entry => entry.SummonDataId == SummonDataId);

    public static unsafe BeastmasterGaugeSnapshot Read()
    {
        if (!DalamudApi.ClientState.IsLoggedIn)
        {
            return Unavailable("未登录");
        }

        if (DalamudApi.PlayerState.ClassJob.RowId != BeastmasterClassJobId)
        {
            return Unavailable("当前职业不是驯兽师");
        }

        var manager = JobGaugeManager.Instance();
        if (manager == null)
        {
            return Unavailable("JobGaugeManager.Instance() 不可用");
        }

        var bytes = new byte[RawGaugeLength];
        var address = (nint)(&manager->CurrentGauge);
        var source = (byte*)address;
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = source[index];
        }

        var (summonDataId, summonName) = FindSummon();
        var player = DalamudApi.ObjectTable.LocalPlayer;
        var hasWhiteStatus = player?.StatusList.Any(status => status.StatusId == WhiteStatusId) == true;
        var hasPurpleStatus = player?.StatusList.Any(status => status.StatusId == PurpleStatusId) == true;
        return new BeastmasterGaugeSnapshot(
            true,
            "读取正常",
            bytes,
            address,
            summonDataId,
            summonName,
            hasWhiteStatus,
            hasPurpleStatus);
    }

    public static BeastmasterGaugeSnapshot ReadRaw() => Read();

    public string FormatRawBytes()
        => Bytes.Length == 0
            ? "不可用"
            : string.Join(' ', Bytes.Select(value => value.ToString("X2")));

    public static BeastmasterGaugeSnapshot Unavailable(string status)
        => new(false, status, Array.Empty<byte>(), nint.Zero);

    private static (uint DataId, string Name) FindSummon()
    {
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
        {
            return (0, "");
        }

        foreach (var obj in DalamudApi.ObjectTable)
        {
            if (obj is IBattleChara summon
                && summon.ObjectKind == ObjectKind.BattleNpc
                && summon.OwnerId == player.EntityId
                && summon.EntityId != player.EntityId
                && summon is ICharacter character
                && character.CurrentHp > 0
                && summon.BaseId is >= 18916 and <= 18965)
            {
                return (summon.BaseId, summon.Name.TextValue);
            }
        }

        return (0, "");
    }

    private byte GetByte(int offset)
        => Bytes.Length > offset ? Bytes[offset] : (byte)0;
}
