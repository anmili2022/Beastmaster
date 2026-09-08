namespace Beastmaster;

public enum BeastmasterObjectiveType
{
    Quest,
    Monster,
    Fate,
    Duty,
    Item,
    TalkToNpc,
    Explore,
}

public sealed record BeastmasterObjective(
    string Key,
    string Name,
    BeastmasterObjectiveType Type,
    string Description);

public sealed record BeastmasterStage(
    string Key,
    string Name,
    string Summary,
    IReadOnlyList<BeastmasterObjective> Objectives);

[Serializable]
public sealed class BeastmasterCharacterProgress
{
    public HashSet<string> CompletedObjectives { get; set; } = new(StringComparer.Ordinal);
}
