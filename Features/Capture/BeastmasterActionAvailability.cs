namespace Beastmaster;

public sealed record BeastmasterActionAvailability(
    uint ActionId,
    string ActionName,
    bool CanUse,
    string Reason);
