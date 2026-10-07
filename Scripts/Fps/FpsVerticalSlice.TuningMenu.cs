using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private Control? _gameplayTuningMenu;
	private Label? _gameplayTuningMetrics;
	private Label? _gameplayTuningStatus;
	private SpinBox? _gameplayTuningSpawnCount;
	private readonly List<Action> _gameplayTuningRefreshControls = new();
	private readonly List<Button> _gameplayTuningSpawnButtons = new();
	private bool _gameplayTuningVisible;
	private float _gameplayTuningRefreshTime;
	// The host supplies sampled frame timing, independently of menu visibility.
	private string _gameplayTuningFrameMetricsText = "Frame sampling warming up...";

	private void BuildGameplayTuningMenu(Control hudRoot)
	{
		TeardownGameplayTuningMenu();
		Control overlay = new() { Name = "GameplayTuningMenu", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		hudRoot.AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_gameplayTuningMenu = overlay;
		ColorRect dim = new() { Color = new Color(0.01f, 0.02f, 0.04f, 0.70f), MouseFilter = Control.MouseFilterEnum.Ignore };
		overlay.AddChild(dim);
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		PanelContainer panel = new() { AnchorLeft = 0.04f, AnchorTop = 0.05f, AnchorRight = 0.96f, AnchorBottom = 0.95f };
		overlay.AddChild(panel);
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.035f, 0.055f, 0.085f, 0.98f),
			BorderColor = new Color(0.24f, 0.65f, 0.86f),
			BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
			ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12,
		});
		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 8);
		panel.AddChild(content);
		HBoxContainer header = new();
		content.AddChild(header);
		Label title = TuningText("Gameplay tuning  ·  ` to close", 20);
		title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		header.AddChild(title);
		AddTuningButton(header, "Close", () => SetGameplayTuningVisible(false));
		content.AddChild(TuningText("Active combat continues while editing. Player controls are suspended. Changes last for this run only."));
		_gameplayTuningMetrics = TuningText("");
		content.AddChild(_gameplayTuningMetrics);
		TabContainer tabs = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		content.AddChild(tabs);
		BuildTuningPopulationTab(AddTuningTab(tabs, "Population"));
		BuildTuningSimulationTab(AddTuningTab(tabs, "Simulation"));
		BuildTuningCrowdContactTab(AddTuningTab(tabs, "Crowd contact"));
		BuildTuningMotionTab(AddTuningTab(tabs, "Movement / deaths"));
		if (UsesFortifications) BuildTuningFortTab(AddTuningTab(tabs, "Fort"));
		VBoxContainer enemies = AddTuningTab(tabs, "Enemies");
		enemies.AddChild(TuningText("Changes apply to existing enemies and future spawns."));
		if (BruiserArchetypeSettings != null) BuildTuningArchetype(enemies, "Cubes / melee", BruiserArchetypeSettings, false);
		if (StrikerArchetypeSettings != null) BuildTuningArchetype(enemies, "Pyramids / ranged", StrikerArchetypeSettings, true);
		BuildTuningPlayerTab(AddTuningTab(tabs, "Player"));
		_gameplayTuningStatus = TuningText("Choose a population, then Add or Replace. Changing the count field alone does not spawn enemies.");
		content.AddChild(_gameplayTuningStatus);
	}

	private void BuildTuningPopulationTab(VBoxContainer page)
	{
		page.AddChild(TuningText("Population is deliberate: Add grows the current crowd; Replace clears enemies and creates the requested crowd."));
		_gameplayTuningSpawnCount = AddTuningNumber(page, "Requested enemies", 1, 2000, 1, () => 100, _ => { });
		HBoxContainer roleRow = new();
		page.AddChild(roleRow);
		roleRow.AddChild(TuningText("Enemy type"));
		OptionButton role = new() { CustomMinimumSize = new Vector2(180, 34) };
		role.AddItem("Cube / melee", (int)FpsEnemyRole.Bruiser);
		role.AddItem("Pyramid / ranged", (int)FpsEnemyRole.Striker);
		role.Selected = DebugSpawnRole == FpsEnemyRole.Striker ? 1 : 0;
		role.ItemSelected += index => DebugSpawnRole = (FpsEnemyRole)role.GetItemId((int)index);
		roleRow.AddChild(role);
		_gameplayTuningRefreshControls.Add(() => role.Selected = DebugSpawnRole == FpsEnemyRole.Striker ? 1 : 0);
		HBoxContainer presets = new();
		page.AddChild(presets);
		foreach (int count in new[] { 100, 500, 1000, 2000 })
			AddTuningButton(presets, count.ToString(), () => _gameplayTuningSpawnCount.Value = count);
		HBoxContainer actions = new();
		page.AddChild(actions);
		_gameplayTuningSpawnButtons.Add(AddTuningButton(actions, "Add", () => SpawnTuningPopulation(false)));
		_gameplayTuningSpawnButtons.Add(AddTuningButton(actions, "Replace", () => SpawnTuningPopulation(true)));
		_gameplayTuningSpawnButtons.Add(AddTuningButton(actions, "Clear", () =>
		{
			DebugClearHorde();
			if (_gameplayTuningStatus != null) _gameplayTuningStatus.Text = "Enemy population cleared.";
		}));
		page.AddChild(TuningText("Use Replace for comparable population runs. Live frame timing includes rendering, gameplay, this menu, and any frame-rate cap."));
		page.AddChild(TuningText("Crowd controls require a defense map with the shared horde enabled."));
		if (UsesFortifications) page.AddChild(TuningText("Enemies spawn in four groups outside the hill, one approaching each gate."));
	}

	private void BuildTuningFortTab(VBoxContainer page)
	{
		page.AddChild(TuningText("Four gates protect the elevated base. Enemies can breach them or pile over the walls."));
		AddTuningNumber(page, "Maximum gate health", 1, 20000, 1, () => HillMapSettings!.GateMaxHealth, v =>
		{
			HillMapSettings!.GateMaxHealth = (float)v;
			foreach (var gate in _defenseGates) gate.Retune((float)v);
		});
		AddTuningCheck(page, "Gates invulnerable", () => _defenseGates.Count > 0 && _defenseGates[0].DebugInvulnerable,
			v => { foreach (var gate in _defenseGates) gate.DebugInvulnerable = v; });
		AddTuningButton(page, "Restore all gates", DebugResetDefenseGates);
		page.AddChild(TuningText("Health tuning preserves damage and breaches. Restore all gates closes breaches and restores full health."));
	}

	private void SpawnTuningPopulation(bool replace)
	{
		int requested = Mathf.Clamp((int)(_gameplayTuningSpawnCount?.Value ?? 100), 1, 2000);
		int added = DebugSpawnHorde(requested, replace);
		if (_gameplayTuningStatus != null)
			_gameplayTuningStatus.Text = $"{(replace ? "Replaced population" : "Added enemies")}: {added}/{requested} spawned. Active enemies: {ActiveEnemyCount}.";
		_gameplayTuningRefreshTime = 0f;
	}

	private void BuildTuningSimulationTab(VBoxContainer page)
	{
		if (HordeSettings is not FpsHordeSettings settings)
		{
			page.AddChild(TuningText("Shared crowd settings are unavailable on this map."));
			return;
		}
		page.AddChild(TuningText("Lower tick rates reduce simulation work but make movement and target decisions less frequent."));
		page.AddChild(TuningText("Separation controls apply when soft collisions are disabled. With collisions enabled, use the Crowd contact tab to tune body spacing."));
		AddTuningCheck(page, "Pause gameplay", () => _pauseRequested, v => { _pauseRequested = v; RefreshCombatState(); });
		AddTuningCheck(page, "Crowd simulation enabled", () => settings.Enabled, v => settings.Enabled = v);
		AddTuningNumber(page, "Simulation Hz", 5, 60, 1, () => settings.SimulationHz, v => settings.SimulationHz = (float)v);
		AddTuningNumber(page, "Decision Hz", 1, 30, 1, () => settings.DecisionHz, v => settings.DecisionHz = (float)v);
		AddTuningNumber(page, "Separation radius", 0, 6, 0.05, () => settings.SeparationRadius, v => settings.SeparationRadius = (float)v);
		AddTuningNumber(page, "Separation strength", 0, 5, 0.05, () => settings.SeparationStrength, v => settings.SeparationStrength = (float)v);
		AddTuningNumber(page, "Neighbor budget", 1, 128, 1, () => settings.MaxSeparationNeighbors, v => settings.MaxSeparationNeighbors = (int)v);
		AddTuningNumber(page, "Player aggro radius", 0, 100, 0.1, () => settings.PlayerAggroRadius, v => settings.PlayerAggroRadius = (float)v);
	}

	private void BuildTuningCrowdContactTab(VBoxContainer page)
	{
		if (HordeSettings is not FpsHordeSettings settings) return;
		page.AddChild(TuningText("Soft body contact lets the wave compress. Piling lets pressure push enemies up over one another; unsupported enemies fall."));
		AddTuningCheck(page, "Soft collisions", () => settings.SoftCollisionEnabled, v => settings.SoftCollisionEnabled = v);
		AddTuningCheck(page, "Allow climbing", () => settings.StackingEnabled, v => settings.StackingEnabled = v);
		AddTuningNumber(page, "Contact stiffness", 0.05, 1, 0.05, () => settings.ContactStiffness, v => settings.ContactStiffness = (float)v);
		AddTuningNumber(page, "Forward push", 0.1, 3, 0.05, () => settings.CrowdPushStrength, v => settings.CrowdPushStrength = (float)v);
		AddTuningNumber(page, "Climb speed (m/s)", 0, 8, 0.1, () => settings.ClimbSpeed, v => settings.ClimbSpeed = (float)v);
		AddTuningNumber(page, "Climb height above terrain (m)", 0, 12, 0.25, () => settings.MaxPileHeight, v => settings.MaxPileHeight = (float)v);
		AddTuningNumber(page, "Gravity (m/s²)", 1, 40, 0.5, () => settings.Gravity, v => settings.Gravity = (float)v);
		AddTuningNumber(page, "Contact passes", 1, 4, 1, () => settings.ContactIterations, v => settings.ContactIterations = (int)v);
		AddTuningNumber(page, "Contact neighbor budget", 8, 64, 1, () => settings.ContactNeighborBudget, v => settings.ContactNeighborBudget = (int)v);
		page.AddChild(TuningText("Higher contact passes and neighbor budgets cost more simulation time. Disabling climbing stops new ascent; existing piles keep their support until bodies move or die."));
	}

	private void BuildTuningMotionTab(VBoxContainer page)
	{
		if (HordeSettings is not FpsHordeSettings settings) return;
		AddTuningCheck(page, "Rolling cubes", () => settings.RollingEnabled, v => settings.RollingEnabled = v);
		AddTuningNumber(page, "Pause between rolls (s)", 0, 0.3, 0.005, () => settings.RollingPauseSeconds, v => settings.RollingPauseSeconds = (float)v);
		AddTuningNumber(page, "Minimum roll duration (s, 0 = automatic)", 0, 1, 0.01, () => settings.RollingDurationSeconds, v => settings.RollingDurationSeconds = (float)v);
		page.AddChild(TuningText("Cubes roll a quarter turn per body width and pause between steps. Blocked cubes stop rolling; crowd pressure can still push them."));
		AddTuningCheck(page, "Tumbling corpses", () => settings.CorpseEnabled, v => settings.CorpseEnabled = v);
		AddTuningNumber(page, "Maximum corpses", 1, 256, 1, () => settings.CorpseCapacity, v => settings.CorpseCapacity = (int)v);
		AddTuningNumber(page, "Corpse lifetime (s)", 0.1, 15, 0.1, () => settings.CorpseLifetimeSeconds, v => settings.CorpseLifetimeSeconds = (float)v);
		AddTuningNumber(page, "Base launch speed", 0, 24, 0.25, () => settings.CorpseBaseLaunchSpeed, v => settings.CorpseBaseLaunchSpeed = (float)v);
		AddTuningNumber(page, "Overkill launch strength", 0, 5, 0.1, () => settings.CorpseOverkillSpeedScale, v => settings.CorpseOverkillSpeedScale = (float)v);
		AddTuningNumber(page, "Maximum directional launch speed", 0, 40, 0.5, () => settings.CorpseMaxLaunchSpeed, v => settings.CorpseMaxLaunchSpeed = (float)v);
		page.AddChild(TuningText("The killing hit launches the body away from its source. Excess damage beyond remaining health increases launch speed. The oldest corpse is recycled when the limit is reached."));
		if (DefenseSettings != null)
		{
			AddTuningNumber(page, "Base shape drop chance (%)", 0, 100, 0.1, () => DefenseSettings.WeaponDropChance * 100f, v => DefenseSettings.WeaponDropChance = (float)v / 100f);
			AddTuningNumber(page, "Base modulator drop chance (%)", 0, 100, 0.1, () => DefenseSettings.ModulatorDropChance * 100f, v => DefenseSettings.ModulatorDropChance = (float)v / 100f);
			page.AddChild(TuningText("Drop chances are independent and still receive equipped Luck bonuses."));
		}
	}

	private void BuildTuningArchetype(VBoxContainer page, string name, FpsEnemyArchetypeSettings settings, bool ranged)
	{
		page.AddChild(TuningText(name, 18));
		void Apply(Action update) { update(); ApplyHordeArchetypeChangesLive(); }
		AddTuningNumber(page, "Max health", 1, 500, 0.1, () => settings.MaxHealth, v => Apply(() => settings.MaxHealth = (float)v));
		AddTuningNumber(page, "Move speed", 0, 25, 0.05, () => settings.MoveSpeed, v => Apply(() => settings.MoveSpeed = (float)v));
		AddTuningNumber(page, ranged ? "Projectile damage" : "Contact damage", 0, 100, 0.1,
			() => ranged ? settings.ProjectileDamage : settings.AttackDamage,
			v => Apply(() => { if (ranged) settings.ProjectileDamage = (float)v; else settings.AttackDamage = (float)v; }));
		AddTuningNumber(page, "Attack cooldown (s)", 0.05, 10, 0.01, () => settings.AttackCooldownSeconds, v => Apply(() => settings.AttackCooldownSeconds = (float)v));
	}

	private void BuildTuningPlayerTab(VBoxContainer page)
	{
		AddTuningCheck(page, "Player invulnerable", () => DebugPlayerInvulnerable, v => DebugPlayerInvulnerable = v);
		AddTuningCheck(page, "Defense core invulnerable", () => DebugCoreInvulnerable, v => DebugCoreInvulnerable = v);
		AddTuningNumber(page, "Player base move speed", 0.1, 40, 0.1, () => _debugBaseMoveSpeed, v =>
		{
			_debugBaseMoveSpeed = (float)v;
			var weapon = GetCurrentWeapon();
			if (weapon != null) ApplyFpsModulatorPassives(weapon);
			else _player.MoveSpeed = (float)v;
		});
		page.AddChild(TuningText("Move speed edits the base speed; equipped movement bonuses still apply. Invulnerability prevents new damage; it does not restore health."));
		AddTuningButton(page, "Open stat / loadout editor (pauses)", () =>
		{
			SetGameplayTuningVisible(false);
			_pauseRequested = true;
			OnDebugPanelPressed();
		});
	}

	private void SetGameplayTuningVisible(bool visible)
	{
		_gameplayTuningVisible = visible && IsInstanceValid(_gameplayTuningMenu);
		if (_gameplayTuningMenu != null) _gameplayTuningMenu.Visible = _gameplayTuningVisible;
		if (_gameplayTuningVisible)
		{
			foreach (Action refresh in _gameplayTuningRefreshControls) refresh();
			_gameplayTuningRefreshTime = 0f;
			TickGameplayTuningMenu(0f);
		}
		RefreshCombatState();
	}

	private void TickGameplayTuningMenu(float dt)
	{
		if (!_gameplayTuningVisible || _gameplayTuningMetrics == null) return;
		_gameplayTuningRefreshTime -= dt;
		if (_gameplayTuningRefreshTime > 0f) return;
		_gameplayTuningRefreshTime = 0.25f;
		FpsHordeManager? horde = HordeManager;
		string state = !IsCombatRunning() ? "Paused / ended" : horde?.Settings.Enabled == false ? "Crowd disabled" : "Live";
		_gameplayTuningMetrics.Text = horde == null
			? $"Active enemies: {ActiveEnemyCount}  |  Shared horde unavailable\n{_gameplayTuningFrameMetricsText}"
			: $"{state}  |  Active enemies: {ActiveEnemyCount}  |  Rendered: {horde.RenderedInstanceCount}  |  Limit: {Mathf.Min(2000, horde.Capacity)}\nLast simulation tick: {horde.LastSimulationMilliseconds:F2} ms  |  Raised enemies: {horde.StackedEnemyCount}  |  Highest feet: {horde.HighestPileFeet:F1} m\n{_gameplayTuningFrameMetricsText}";
		foreach (Button button in _gameplayTuningSpawnButtons) button.Disabled = horde == null;
	}

	private void TeardownGameplayTuningMenu()
	{
		if (IsInstanceValid(_gameplayTuningMenu)) _gameplayTuningMenu!.QueueFree();
		_gameplayTuningMenu = null;
		_gameplayTuningMetrics = null;
		_gameplayTuningStatus = null;
		_gameplayTuningSpawnCount = null;
		_gameplayTuningVisible = false;
		_gameplayTuningRefreshControls.Clear();
		_gameplayTuningSpawnButtons.Clear();
	}

	private static VBoxContainer AddTuningTab(TabContainer tabs, string title)
	{
		ScrollContainer scroll = new() { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		tabs.AddChild(scroll);
		VBoxContainer page = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		page.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(page);
		return page;
	}

	private static Label TuningText(string text, int fontSize = 14)
	{
		Label label = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		label.AddThemeFontSizeOverride("font_size", fontSize);
		return label;
	}

	private static Button AddTuningButton(Node parent, string text, Action action)
	{
		Button button = new() { Text = text, CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.Pressed += action;
		parent.AddChild(button);
		return button;
	}

	private SpinBox AddTuningNumber(VBoxContainer parent, string text, double min, double max, double step, Func<double> read, Action<double> write)
	{
		HBoxContainer row = new();
		parent.AddChild(row);
		row.AddChild(TuningText(text));
		SpinBox number = new() { MinValue = min, MaxValue = max, Step = step, Value = read(), CustomMinimumSize = new Vector2(130, 34) };
		row.AddChild(number);
		number.ValueChanged += value => write(value);
		// Population entry is a command parameter, not a live setting.
		if (text != "Requested enemies") _gameplayTuningRefreshControls.Add(() => number.SetValueNoSignal(read()));
		return number;
	}

	private void AddTuningCheck(VBoxContainer parent, string text, Func<bool> read, Action<bool> write)
	{
		CheckBox check = new() { Text = text, ButtonPressed = read() };
		parent.AddChild(check);
		check.Toggled += value => write(value);
		_gameplayTuningRefreshControls.Add(() => check.SetPressedNoSignal(read()));
	}
}
