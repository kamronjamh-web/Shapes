using Godot;
using SuperGatchaRebuild.UI;

namespace SuperGatchaRebuild.Managers;

// Minimal save bridge kept separate so higher-level systems can call one save entry point.
public partial class SaveManager : Node, IPlayerIdentityService
{
    [Export] public string SaveFilePath = "user://save_manager.cfg";

    private const string MetaSection = "_meta";
    private const string SchemaVersionKey = "schema_version";
    private const int CurrentSchemaVersion = 1;
    private const string IdentitySection = "identity";

    public string PlayerName { get; set; } = "Player";
    public int AvatarId { get; set; }

    public override void _Ready()
    {
        LoadIdentity();
    }

    public void SaveIdentity()
    {
        ConfigFile cfg = new();
        _ = cfg.Load(SaveFilePath);

        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        cfg.SetValue(IdentitySection, "player_name", PlayerName);
        cfg.SetValue(IdentitySection, "avatar_id", AvatarId);

        Error err = cfg.Save(SaveFilePath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"Failed saving identity in SaveManager: {err}");
        }
    }

    public void SaveGame()
    {
        SaveIdentity();
    }

    private void LoadIdentity()
    {
        ConfigFile cfg = new();
        if (cfg.Load(SaveFilePath) != Error.Ok)
        {
            return;
        }

        if (EnsureSchema(cfg))
        {
            _ = cfg.Save(SaveFilePath);
        }

        PlayerName = cfg.GetValue(IdentitySection, "player_name", PlayerName).AsString();
        AvatarId = cfg.GetValue(IdentitySection, "avatar_id", AvatarId).AsInt32();
    }

    private static bool EnsureSchema(ConfigFile cfg)
    {
        int version = Mathf.Max(0, cfg.GetValue(MetaSection, SchemaVersionKey, 0).AsInt32());
        if (version == CurrentSchemaVersion)
        {
            return false;
        }

        // Legacy schema used the same identity keys and only lacked metadata.
        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        return true;
    }
}
