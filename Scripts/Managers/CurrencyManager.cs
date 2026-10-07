using Godot;

namespace SuperGatchaRebuild.Managers;

// Signal-driven currency state for hub/gacha UI.
public partial class CurrencyManager : Node
{
    [Signal]
    public delegate void RollCurrencyChangedEventHandler(int amount);

    [Signal]
    public delegate void FatedRollCurrencyChangedEventHandler(int amount);

    [Signal]
    public delegate void CoinsChangedEventHandler(int amount);

    [Export] public int InitialRollCurrency = 0;
    [Export] public int InitialFatedRollCurrency = 0;
    [Export] public int InitialCoins = 0;
    [Export] public string SaveFilePath = "user://currency_state.cfg";

    private const string MetaSection = "_meta";
    private const string SchemaVersionKey = "schema_version";
    private const int CurrentSchemaVersion = 1;
    private const string CurrencySection = "currency";

    public int RollCurrency { get; private set; }
    public int FatedRollCurrency { get; private set; }
    public int Coins { get; private set; }

    public override void _Ready()
    {
        LoadState();
    }

    public void SetRollCurrency(int value)
    {
        value = Mathf.Max(0, value);
        if (RollCurrency == value)
        {
            return;
        }

        RollCurrency = value;
        EmitSignal(SignalName.RollCurrencyChanged, RollCurrency);
        SaveState();
    }

    public void AddRollCurrency(int amount) => SetRollCurrency(RollCurrency + amount);

    public bool TryConsumeRollCurrency(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (RollCurrency < amount)
        {
            return false;
        }

        SetRollCurrency(RollCurrency - amount);
        return true;
    }

    public void SetFatedRollCurrency(int value)
    {
        value = Mathf.Max(0, value);
        if (FatedRollCurrency == value)
        {
            return;
        }

        FatedRollCurrency = value;
        EmitSignal(SignalName.FatedRollCurrencyChanged, FatedRollCurrency);
        SaveState();
    }

    public void AddFatedRollCurrency(int amount) => SetFatedRollCurrency(FatedRollCurrency + amount);

    public bool TryConsumeFatedRollCurrency(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (FatedRollCurrency < amount)
        {
            return false;
        }

        SetFatedRollCurrency(FatedRollCurrency - amount);
        return true;
    }

    public void SetCoins(int value)
    {
        value = Mathf.Max(0, value);
        if (Coins == value)
        {
            return;
        }

        Coins = value;
        EmitSignal(SignalName.CoinsChanged, Coins);
        SaveState();
    }

    public void AddCoins(int amount) => SetCoins(Coins + amount);

    public bool TryConsumeCoins(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (Coins < amount)
        {
            return false;
        }

        SetCoins(Coins - amount);
        return true;
    }

    private void LoadState()
    {
        ConfigFile cfg = new();
        if (cfg.Load(SaveFilePath) != Error.Ok)
        {
            RollCurrency = Mathf.Max(0, InitialRollCurrency);
            FatedRollCurrency = Mathf.Max(0, InitialFatedRollCurrency);
            Coins = Mathf.Max(0, InitialCoins);
            return;
        }

        bool migrated = EnsureSchema(cfg);

        RollCurrency = Mathf.Max(0, cfg.GetValue(CurrencySection, "roll", InitialRollCurrency).AsInt32());
        FatedRollCurrency = Mathf.Max(0, cfg.GetValue(CurrencySection, "fated", InitialFatedRollCurrency).AsInt32());
        Coins = Mathf.Max(0, cfg.GetValue(CurrencySection, "coins", InitialCoins).AsInt32());

        if (migrated)
        {
            SaveState();
        }
    }

    private void SaveState()
    {
        ConfigFile cfg = new();
        _ = cfg.Load(SaveFilePath);

        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        cfg.SetValue(CurrencySection, "roll", RollCurrency);
        cfg.SetValue(CurrencySection, "fated", FatedRollCurrency);
        cfg.SetValue(CurrencySection, "coins", Coins);

        Error err = cfg.Save(SaveFilePath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"Failed saving currency state: {err}");
        }
    }

    private static bool EnsureSchema(ConfigFile cfg)
    {
        int version = Mathf.Max(0, cfg.GetValue(MetaSection, SchemaVersionKey, 0).AsInt32());
        if (version == CurrentSchemaVersion)
        {
            return false;
        }

        // Legacy schema used the same currency keys and only lacked metadata.
        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        return true;
    }
}
