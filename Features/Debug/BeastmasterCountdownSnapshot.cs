using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Beastmaster;

public sealed record BeastmasterCountdownSnapshot(
    bool Available,
    bool Active,
    float TimeRemaining,
    uint Initiator,
    string Status)
{
    public static unsafe BeastmasterCountdownSnapshot Read()
    {
        var framework = Framework.Instance();
        if (framework == null || framework->UIModule == null)
        {
            return Unavailable("Framework 或 UIModule 不可用");
        }

        var agentModule = framework->UIModule->GetAgentModule();
        if (agentModule == null)
        {
            return Unavailable("AgentModule 不可用");
        }

        var agent = agentModule->GetAgentByInternalId(AgentId.CountDownSettingDialog);
        if (agent == null)
        {
            return Unavailable("CountDownSettingDialog Agent 不可用");
        }

        var countdown = (CountdownAgent*)agent;
        var timer = countdown->Timer;
        if (!float.IsFinite(timer) || timer < 0f || timer > 3600f)
        {
            return Unavailable($"倒计时数值异常：{timer}");
        }

        var active = countdown->Active != 0;
        return new(true, active, active ? timer : 0f, countdown->Initiator, "读取正常");
    }

    private static BeastmasterCountdownSnapshot Unavailable(string status)
        => new(false, false, 0f, 0, status);

    [StructLayout(LayoutKind.Explicit)]
    private struct CountdownAgent
    {
        [FieldOffset(0x28)] public float Timer;
        [FieldOffset(0x38)] public byte Active;
        [FieldOffset(0x3C)] public uint Initiator;
    }
}
