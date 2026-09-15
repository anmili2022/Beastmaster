namespace Beastmaster;

public sealed class BeastmasterProgressService
{
    private readonly BeastmasterConfiguration configuration;

    public BeastmasterProgressService(BeastmasterConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public string CurrentCharacterKey
    {
        get
        {
            if (!DalamudApi.ClientState.IsLoggedIn)
            {
                return string.Empty;
            }

            var contentId = DalamudApi.PlayerState.ContentId;
            if (contentId != 0)
            {
                return contentId.ToString();
            }

            var player = DalamudApi.ObjectTable.LocalPlayer;
            if (player == null)
            {
                return string.Empty;
            }

            var world = player.HomeWorld.Value.Name.ExtractText();
            return string.IsNullOrWhiteSpace(world)
                ? player.Name.TextValue
                : $"{player.Name.TextValue}@{world}";
        }
    }

    public string CurrentCharacterLabel
    {
        get
        {
            if (!DalamudApi.ClientState.IsLoggedIn)
            {
                return "未登录";
            }

            var player = DalamudApi.ObjectTable.LocalPlayer;
            if (player == null)
            {
                return "角色数据加载中";
            }

            var world = player.HomeWorld.Value.Name.ExtractText();
            return string.IsNullOrWhiteSpace(world)
                ? player.Name.TextValue
                : $"{player.Name.TextValue}@{world}";
        }
    }

    public bool IsCompleted(string objectiveKey)
    {
        var characterKey = CurrentCharacterKey;
        return characterKey.Length > 0
            && configuration.ProgressByCharacter.TryGetValue(characterKey, out var progress)
            && progress.CompletedObjectives.Contains(objectiveKey);
    }

    public void SetCompleted(string objectiveKey, bool completed)
    {
        var characterKey = CurrentCharacterKey;
        if (characterKey.Length == 0)
        {
            return;
        }

        if (!configuration.ProgressByCharacter.TryGetValue(characterKey, out var progress))
        {
            if (!completed)
            {
                return;
            }

            progress = new BeastmasterCharacterProgress();
            configuration.ProgressByCharacter[characterKey] = progress;
        }

        var changed = completed
            ? progress.CompletedObjectives.Add(objectiveKey)
            : progress.CompletedObjectives.Remove(objectiveKey);
        if (changed)
        {
            configuration.Save();
        }
    }

    public int ReplaceCatalogProgress(IReadOnlySet<int> unlockedNumbers)
    {
        var characterKey = CurrentCharacterKey;
        if (characterKey.Length == 0)
        {
            return 0;
        }

        if (!configuration.ProgressByCharacter.TryGetValue(characterKey, out var progress))
        {
            progress = new BeastmasterCharacterProgress();
            configuration.ProgressByCharacter[characterKey] = progress;
        }

        var changed = 0;
        foreach (var entry in BeastmasterCatalog.Entries)
        {
            var shouldBeCompleted = unlockedNumbers.Contains(entry.Number);
            var isCompleted = progress.CompletedObjectives.Contains(entry.Key);
            if (shouldBeCompleted == isCompleted)
            {
                continue;
            }

            if (shouldBeCompleted)
            {
                progress.CompletedObjectives.Add(entry.Key);
            }
            else
            {
                progress.CompletedObjectives.Remove(entry.Key);
            }

            changed++;
        }

        if (changed > 0)
        {
            configuration.Save();
        }

        return changed;
    }

    public bool IsAchievementCompleted(int achievementId)
    {
        var characterKey = CurrentCharacterKey;
        return characterKey.Length > 0
            && configuration.ProgressByCharacter.TryGetValue(characterKey, out var progress)
            && progress.CompletedAchievements.Contains(achievementId);
    }

    public int ReplaceAchievementProgress(IReadOnlySet<int> completedIds)
    {
        var characterKey = CurrentCharacterKey;
        if (characterKey.Length == 0)
        {
            return 0;
        }

        if (!configuration.ProgressByCharacter.TryGetValue(characterKey, out var progress))
        {
            progress = new BeastmasterCharacterProgress();
            configuration.ProgressByCharacter[characterKey] = progress;
        }

        var changed = 0;
        foreach (var group in BeastmasterAchievementCatalog.Groups)
        {
            foreach (var achievementId in group.AchievementIds)
            {
                var shouldBeCompleted = completedIds.Contains(achievementId);
                var isCompleted = progress.CompletedAchievements.Contains(achievementId);
                if (shouldBeCompleted == isCompleted)
                {
                    continue;
                }

                if (shouldBeCompleted)
                {
                    progress.CompletedAchievements.Add(achievementId);
                }
                else
                {
                    progress.CompletedAchievements.Remove(achievementId);
                }

                changed++;
            }
        }

        if (changed > 0)
        {
            configuration.Save();
        }

        return changed;
    }

    public BeastmasterBeastProgress? GetBeastProgress(int number)
    {
        var characterKey = CurrentCharacterKey;
        return characterKey.Length > 0
            && configuration.ProgressByCharacter.TryGetValue(characterKey, out var progress)
            && progress.BeastProgress.TryGetValue(number, out var beastProgress)
                ? beastProgress
                : null;
    }

    public int UpdateBeastProgress(IReadOnlyDictionary<int, (int Level, int Experience, int ExperienceRequired)> updates)
    {
        var characterKey = CurrentCharacterKey;
        if (characterKey.Length == 0 || updates.Count == 0)
        {
            return 0;
        }

        if (!configuration.ProgressByCharacter.TryGetValue(characterKey, out var progress))
        {
            progress = new BeastmasterCharacterProgress();
            configuration.ProgressByCharacter[characterKey] = progress;
        }

        progress.BeastProgress ??= [];
        var changed = 0;
        var now = DateTime.UtcNow;
        foreach (var (number, value) in updates)
        {
            var validExperience = value.Level == 25
                ? value.Experience == 0 && value.ExperienceRequired == 0
                : value.Experience is >= 0 and <= 999999
                    && value.ExperienceRequired is >= 1 and <= 999999
                    && value.Experience < value.ExperienceRequired;
            if (number is < 1 or > 50 || value.Level is < 1 or > 25 || !validExperience)
            {
                continue;
            }

            if (progress.BeastProgress.TryGetValue(number, out var current)
                && current.Level == value.Level
                && current.Experience == value.Experience
                && current.ExperienceRequired == value.ExperienceRequired)
            {
                continue;
            }

            progress.BeastProgress[number] = new BeastmasterBeastProgress
            {
                Level = value.Level,
                Experience = value.Experience,
                ExperienceRequired = value.ExperienceRequired,
                UpdatedUtc = now,
            };
            changed++;
        }

        if (changed > 0)
        {
            configuration.Save();
        }

        return changed;
    }

    public int GetCompletedCount(BeastmasterStage stage)
        => stage.Objectives.Count(objective => IsCompleted(objective.Key));
}
