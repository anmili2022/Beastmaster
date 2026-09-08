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

    public int GetCompletedCount(BeastmasterStage stage)
        => stage.Objectives.Count(objective => IsCompleted(objective.Key));
}
