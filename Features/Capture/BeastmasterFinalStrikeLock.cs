namespace Beastmaster;

public static class BeastmasterFinalStrikeLock
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
    private static DateTime finalStrikeBlockedUntilUtc = DateTime.MinValue;
    private static DateTime releaseBlockedUntilUtc = DateTime.MinValue;

    public static void RecordFinalStrike(DateTime now)
        => finalStrikeBlockedUntilUtc = now.Add(Duration);

    public static void RecordRelease(DateTime now)
        => releaseBlockedUntilUtc = now.Add(Duration);

    public static bool IsBlocked(uint actionId, DateTime now)
        => (actionId == 44891 && now < releaseBlockedUntilUtc)
            || (actionId is 44884 or 44887 or 44888 or 44889 or 47093
                && now < finalStrikeBlockedUntilUtc);

    public static double RemainingSeconds(uint actionId, DateTime now)
        => Math.Max(0d, ((actionId == 44891 ? releaseBlockedUntilUtc : finalStrikeBlockedUntilUtc) - now).TotalSeconds);

    public static string GetBlockReason(uint actionId, DateTime now)
        => actionId == 44891
            ? $"释放保护中，还剩 {RemainingSeconds(actionId, now):0.#} 秒"
            : $"最后一击保护中，还剩 {RemainingSeconds(actionId, now):0.#} 秒";
}
