using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Beastmaster;

public sealed unsafe class BeastmasterNotebookProgressService : IDisposable
{
    private const string AddonName = "XBMMonsterNotebook";
    private const int NumberTextIndex = 229;
    private const int IconIndex = 231;
    private const int LevelIndex = 258;
    private const int ExperienceIndex = 261;
    private const int ExperienceRequiredIndex = 262;

    private readonly BeastmasterProgressService progressService;
    private long nextReadAt;
    private string lastSnapshot = string.Empty;
    private string stableSnapshot = string.Empty;

    public BeastmasterNotebookProgressService(BeastmasterProgressService progressService)
    {
        this.progressService = progressService;
    }

    public void Start() => DalamudApi.Framework.Update += OnFrameworkUpdate;

    public void Dispose() => DalamudApi.Framework.Update -= OnFrameworkUpdate;

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            if (Environment.TickCount64 < nextReadAt)
            {
                return;
            }

            nextReadAt = Environment.TickCount64 + 250;
            var characterKey = progressService.CurrentCharacterKey;
            var addon = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName(AddonName, 1).Address;
            if (characterKey.Length == 0 || addon == null || !addon->IsVisible || addon->AtkValues == null
                || addon->AtkValuesCount <= ExperienceRequiredIndex)
            {
                lastSnapshot = string.Empty;
                stableSnapshot = string.Empty;
                return;
            }

            var number = ReadNumber(addon->AtkValues[NumberTextIndex]);
            var icon = ReadNumber(addon->AtkValues[IconIndex]);
            var level = ReadNumber(addon->AtkValues[LevelIndex]);
            var experience = ReadNumber(addon->AtkValues[ExperienceIndex]);
            var required = ReadNumber(addon->AtkValues[ExperienceRequiredIndex]);
            if (number is < 1 or > 50 || icon != 242000 + number || level is < 1 or > 25)
            {
                return;
            }

            if (level == 25)
            {
                experience = 0;
                required = 0;
            }
            else if (required is < 1 or > 999999 || experience >= required)
            {
                return;
            }

            var snapshot = $"{characterKey}|{number}:{level}:{experience}:{required}";
            if (snapshot != lastSnapshot)
            {
                lastSnapshot = snapshot;
                return;
            }

            if (snapshot == stableSnapshot)
            {
                return;
            }

            stableSnapshot = snapshot;
            progressService.UpdateBeastProgress(new Dictionary<int, (int, int, int)>
            {
                [(int)number] = ((int)level, (int)experience, (int)required),
            });
        }
        catch (Exception ex)
        {
            lastSnapshot = string.Empty;
            stableSnapshot = string.Empty;
            DalamudApi.Log.Warning(ex, "读取魔兽图鉴等级经验失败。");
        }
    }

    private static uint ReadNumber(AtkValue value)
        => value.TypeCode() switch
        {
            3 when value.Int >= 0 => (uint)value.Int,
            4 or 5 => value.UInt,
            8 or 10 when uint.TryParse(value.String.ToString(), out var parsed) => parsed,
            _ => uint.MaxValue,
        };
}
