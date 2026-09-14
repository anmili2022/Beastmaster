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
    public Dictionary<int, BeastmasterBeastProgress> BeastProgress { get; set; } = [];
}

[Serializable]
public sealed class BeastmasterBeastProgress
{
    public int Level { get; set; }
    public int Experience { get; set; }
    public int ExperienceRequired { get; set; } = 100;
    public DateTime UpdatedUtc { get; set; }
}
