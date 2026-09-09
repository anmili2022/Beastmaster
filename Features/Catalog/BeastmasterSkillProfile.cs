namespace Beastmaster;

public sealed record BeastmasterSkillProfile(
    int CatalogNumber,
    string BeastName,
    uint SummonDataId,
    BeastmasterAttribute Attribute,
    uint UltimateActionId,
    uint ReleaseActionId);
