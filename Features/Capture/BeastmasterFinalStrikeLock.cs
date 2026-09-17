namespace Beastmaster;

public static class BeastmasterFinalStrikeLock
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
    private static DateTime blockedUntilUtc = DateTime.MinValue;

    public static void Record(DateTime now)
        => blockedUntilUtc = now.Add(Duration);

    public static bool IsBlocked(uint actionId, DateTime now)
        => now < blockedUntilUtc && actionId is 44884 or 44887 or 44888 or 44889 or 47093;

    public static double RemainingSeconds(DateTime now)
        => Math.Max(0d, (blockedUntilUtc - now).TotalSeconds);
}
