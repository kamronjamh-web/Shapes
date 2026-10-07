using Godot;
using System.Text;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.UI;

namespace SuperGatchaRebuild;

public partial class Main
{
	private bool _arenaPauseMenuVisible;
	private Control _arenaPauseOverlay = null!;
	private Label _arenaPauseSummaryLabel = null!;
	private Button _arenaPauseDebugButton = null!;
	private RichTextLabel _arenaPauseGlobalStatsLabel = null!;
	private RichTextLabel _arenaPauseWeaponStatsLabel = null!;
	private Control? _arenaDebugPanel;
	private ArenaDebugPanelUI? _arenaDebugPanelUi;

	private bool IsArenaGameplayPaused()
	{
		return _pauseForItemChoice || _arenaPauseMenuVisible;
	}

	private void HandleArenaPauseToggle()
	{
		if (_pauseForItemChoice || !Input.IsActionJustPressed("ui_cancel"))
		{
			return;
		}

		if (IsArenaMerchantInspectVisible())
		{
			CloseArenaMerchantInspect();
			return;
		}

		if (IsRunModWorkshopVisible())
		{
			CloseRunModWorkshop();
			return;
		}

		if (IsArenaMerchantVisible())
		{
			CloseArenaMerchantOverlay();
			return;
		}

		SetArenaPauseMenuVisible(!_arenaPauseMenuVisible);
	}

	private void BuildArenaPauseOverlay(Control root)
	{
		_arenaPauseOverlay = new Control();
		_arenaPauseOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_arenaPauseOverlay.MouseFilter = Control.MouseFilterEnum.Stop;
		_arenaPauseOverlay.Visible = false;
		_arenaPauseOverlay.ZIndex = 90;
		root.AddChild(_arenaPauseOverlay);

		ColorRect backdrop = new();
		backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		backdrop.Color = new Color(0.02f, 0.03f, 0.08f, 0.80f);
		backdrop.Material = CreateRadialOverlayMaterial(0.18f);
		backdrop.MouseFilter = Control.MouseFilterEnum.Ignore;
		_arenaPauseOverlay.AddChild(backdrop);

		Control shell = CreateHudCard(
			Vector2.Zero,
			Vector2.Zero,
			new Color(0.42f, 0.66f, 0.96f, 0.32f),
			new Color(0.02f, 0.05f, 0.10f, 0.94f));
		shell.AnchorLeft = 0.07f;
		shell.AnchorTop = 0.10f;
		shell.AnchorRight = 0.93f;
		shell.AnchorBottom = 0.90f;
		shell.OffsetLeft = 0f;
		shell.OffsetTop = 0f;
		shell.OffsetRight = 0f;
		shell.OffsetBottom = 0f;
		shell.MouseFilter = Control.MouseFilterEnum.Stop;
		_arenaPauseOverlay.AddChild(shell);

		Control leftPanel = CreateHudCard(
			Vector2.Zero,
			Vector2.Zero,
			new Color(0.22f, 0.86f, 0.76f, 0.30f),
			new Color(0.01f, 0.07f, 0.08f, 0.92f));
		leftPanel.AnchorLeft = 0f;
		leftPanel.AnchorTop = 0f;
		leftPanel.AnchorRight = 0f;
		leftPanel.AnchorBottom = 1f;
		leftPanel.OffsetLeft = 24f;
		leftPanel.OffsetTop = 24f;
		leftPanel.OffsetRight = 304f;
		leftPanel.OffsetBottom = -24f;
		leftPanel.MouseFilter = Control.MouseFilterEnum.Pass;
		shell.AddChild(leftPanel);

		Label title = new();
		title.Position = new Vector2(18f, 20f);
		title.Size = new Vector2(240f, 34f);
		title.Text = "ARENA PAUSED";
		StyleHudLabel(title, 28, new Color(0.88f, 0.97f, 1f, 1f));
		leftPanel.AddChild(title);

		Label subtitle = new();
		subtitle.Position = new Vector2(18f, 58f);
		subtitle.Size = new Vector2(240f, 20f);
		subtitle.Text = "ESC resumes the run.";
		subtitle.AddThemeFontSizeOverride("font_size", 12);
		subtitle.AddThemeColorOverride("font_color", new Color(0.64f, 0.82f, 0.86f, 0.78f));
		leftPanel.AddChild(subtitle);

		_arenaPauseSummaryLabel = new Label();
		_arenaPauseSummaryLabel.Position = new Vector2(18f, 100f);
		_arenaPauseSummaryLabel.Size = new Vector2(240f, 90f);
		_arenaPauseSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_arenaPauseSummaryLabel.AddThemeFontSizeOverride("font_size", 13);
		_arenaPauseSummaryLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.92f, 0.98f, 0.92f));
		_arenaPauseSummaryLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.82f));
		_arenaPauseSummaryLabel.AddThemeConstantOverride("outline_size", 1);
		leftPanel.AddChild(_arenaPauseSummaryLabel);

		Button workshopButton = new();
		workshopButton.Position = new Vector2(18f, 236f);
		workshopButton.Size = new Vector2(240f, 48f);
		workshopButton.Text = "Mod Workshop";
		workshopButton.MouseFilter = Control.MouseFilterEnum.Stop;
		workshopButton.FocusMode = Control.FocusModeEnum.All;
		workshopButton.Pressed += OpenRunModWorkshop;
		StyleArenaPauseButton(
			workshopButton,
			new Color(0.08f, 0.28f, 0.24f, 0.95f),
			new Color(0.42f, 0.98f, 0.84f, 0.56f),
			new Color(0.12f, 0.38f, 0.32f, 1f),
			new Color(0.62f, 1f, 0.90f, 0.90f),
			new Color(0.90f, 1f, 0.96f, 1f));
		leftPanel.AddChild(workshopButton);

		_arenaPauseDebugButton = new Button();
		_arenaPauseDebugButton.Position = new Vector2(18f, 296f);
		_arenaPauseDebugButton.Size = new Vector2(240f, 48f);
		_arenaPauseDebugButton.Text = "Open Debug";
		_arenaPauseDebugButton.MouseFilter = Control.MouseFilterEnum.Stop;
		_arenaPauseDebugButton.FocusMode = Control.FocusModeEnum.All;
		_arenaPauseDebugButton.Pressed += () => SetDebugPanelVisible(!_debugPanelVisible);
		StyleArenaPauseButton(
			_arenaPauseDebugButton,
			new Color(0.15f, 0.23f, 0.38f, 0.95f),
			new Color(0.48f, 0.74f, 1f, 0.65f),
			new Color(0.19f, 0.32f, 0.50f, 1f),
			new Color(0.64f, 0.86f, 1f, 0.90f),
			new Color(0.86f, 0.95f, 1f, 1f));
		leftPanel.AddChild(_arenaPauseDebugButton);

		Button leaveButton = new();
		leaveButton.Position = new Vector2(18f, 356f);
		leaveButton.Size = new Vector2(240f, 48f);
		leaveButton.Text = "Leave Arena";
		leaveButton.MouseFilter = Control.MouseFilterEnum.Stop;
		leaveButton.FocusMode = Control.FocusModeEnum.All;
		leaveButton.Pressed += OnExitArenaPressed;
		StyleArenaPauseButton(
			leaveButton,
			new Color(0.34f, 0.08f, 0.10f, 0.95f),
			new Color(1f, 0.42f, 0.42f, 0.60f),
			new Color(0.48f, 0.10f, 0.12f, 1f),
			new Color(1f, 0.62f, 0.62f, 0.85f),
			new Color(1f, 0.90f, 0.90f, 1f));
		leftPanel.AddChild(leaveButton);

		Label actionHint = new();
		actionHint.Position = new Vector2(18f, 426f);
		actionHint.Size = new Vector2(240f, 80f);
		actionHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		actionHint.Text = "Use the workshop to reroute run modulators, then open debug here when you need live overrides or enemy spawns.";
		actionHint.AddThemeFontSizeOverride("font_size", 12);
		actionHint.AddThemeColorOverride("font_color", new Color(0.70f, 0.84f, 0.84f, 0.74f));
		leftPanel.AddChild(actionHint);

		Control rightPanel = CreateHudCard(
			Vector2.Zero,
			Vector2.Zero,
			new Color(0.56f, 0.78f, 1f, 0.30f),
			new Color(0.02f, 0.06f, 0.12f, 0.94f));
		rightPanel.AnchorLeft = 0f;
		rightPanel.AnchorTop = 0f;
		rightPanel.AnchorRight = 1f;
		rightPanel.AnchorBottom = 1f;
		rightPanel.OffsetLeft = 324f;
		rightPanel.OffsetTop = 24f;
		rightPanel.OffsetRight = -24f;
		rightPanel.OffsetBottom = -24f;
		rightPanel.MouseFilter = Control.MouseFilterEnum.Pass;
		rightPanel.ClipContents = true;
		shell.AddChild(rightPanel);

		VBoxContainer rightContent = new()
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		rightContent.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		rightContent.OffsetLeft = 18f;
		rightContent.OffsetTop = 18f;
		rightContent.OffsetRight = -18f;
		rightContent.OffsetBottom = -18f;
		rightContent.AddThemeConstantOverride("separation", 8);
		rightPanel.AddChild(rightContent);

		Label rightTitle = new()
		{
			CustomMinimumSize = new Vector2(0f, 28f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		rightTitle.Text = "LIVE PLAYER STATS";
		StyleHudLabel(rightTitle, 24, new Color(0.90f, 0.97f, 1f, 1f));
		rightContent.AddChild(rightTitle);

		Label globalHeader = new()
		{
			CustomMinimumSize = new Vector2(0f, 20f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		globalHeader.Text = "Global";
		globalHeader.AddThemeFontSizeOverride("font_size", 13);
		globalHeader.AddThemeColorOverride("font_color", new Color(0.62f, 0.80f, 0.96f, 0.82f));
		rightContent.AddChild(globalHeader);

		_arenaPauseGlobalStatsLabel = new RichTextLabel()
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(0f, 244f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ScrollActive = false,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_arenaPauseGlobalStatsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_arenaPauseGlobalStatsLabel.AddThemeFontSizeOverride("normal_font_size", 14);
		_arenaPauseGlobalStatsLabel.AddThemeColorOverride("default_color", new Color(0.92f, 0.97f, 1f, 0.96f));
		rightContent.AddChild(_arenaPauseGlobalStatsLabel);

		ColorRect divider = new()
		{
			CustomMinimumSize = new Vector2(0f, 1f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		divider.Color = new Color(1f, 1f, 1f, 0.10f);
		rightContent.AddChild(divider);

		Label weaponHeader = new()
		{
			CustomMinimumSize = new Vector2(0f, 22f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		weaponHeader.Text = "Per Weapon";
		weaponHeader.AddThemeFontSizeOverride("font_size", 13);
		weaponHeader.AddThemeColorOverride("font_color", new Color(0.62f, 0.80f, 0.96f, 0.82f));
		rightContent.AddChild(weaponHeader);

		_arenaPauseWeaponStatsLabel = new RichTextLabel()
		{
			MouseFilter = Control.MouseFilterEnum.Pass,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		_arenaPauseWeaponStatsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_arenaPauseWeaponStatsLabel.ScrollActive = true;
		_arenaPauseWeaponStatsLabel.AddThemeFontSizeOverride("normal_font_size", 13);
		_arenaPauseWeaponStatsLabel.AddThemeColorOverride("default_color", new Color(0.90f, 0.96f, 1f, 0.96f));
		rightContent.AddChild(_arenaPauseWeaponStatsLabel);

		BuildArenaMerchantOverlay(shell);
		BuildRunModWorkshopOverlay(shell);

		UpdateArenaPauseOverlayState();
	}

	private void SetArenaPauseMenuVisible(bool visible)
	{
		_arenaPauseMenuVisible = visible;
		if (!visible)
		{
			CloseRunModWorkshop();
			SetDebugPanelVisible(false);
		}

		UpdateArenaPauseOverlayState();
	}

	private void SetDebugPanelVisible(bool visible)
	{
		bool nextVisible = visible && _arenaPauseMenuVisible && IsInstanceValid(_arenaDebugPanel);
		_debugPanelVisible = nextVisible;

		if (_arenaDebugPanelUi != null)
		{
			_arenaDebugPanelUi.SetExternalMenuVisible(nextVisible);
		}
		else if (IsInstanceValid(_arenaDebugPanel))
		{
			_arenaDebugPanel!.Visible = nextVisible;
		}

		UpdateArenaPauseOverlayState();
	}

	private void UpdateArenaPauseOverlayState()
	{
		if (IsInstanceValid(_arenaPauseOverlay))
		{
			_arenaPauseOverlay.Visible = _arenaPauseMenuVisible;
		}

		if (IsInstanceValid(_arenaPauseDebugButton))
		{
			_arenaPauseDebugButton.Text = _debugPanelVisible ? "Close Debug" : "Open Debug";
		}

		if (IsInstanceValid(_arenaPauseSummaryLabel))
		{
			_arenaPauseSummaryLabel.Text =
				$"Wave {_waveNum}  |  Time {FormatArenaTime(_gameTimer)}\n" +
				$"Kills {_kills}  |  Coins {_coins}\n" +
				$"Collected XP {_xp:0}";
		}

		if (IsInstanceValid(_arenaPauseGlobalStatsLabel))
		{
			_arenaPauseGlobalStatsLabel.Text = BuildArenaPauseGlobalStatsText();
		}

		if (IsInstanceValid(_arenaPauseWeaponStatsLabel))
		{
			_arenaPauseWeaponStatsLabel.Text = BuildArenaPauseWeaponStatsText();
		}
	}

	private string BuildArenaPauseGlobalStatsText()
	{
		float moveSpeedPct = _player.MoveSpeed / Mathf.Max(1f, BaseMoveSpeed) * 100f;
		float dodgeChance = GetTotalDodgeChancePercent();
		float critChance = GetTotalCritChancePercent();
		float critDamage = GetDisplayedCritDamagePercent();
		float regen = GetTotalRegenPerSecond();
		float lifeSteal = GetTotalLifeStealPercent();
		float range = GetCurrentWeaponTargetRange() * RangeDisplayScale;
		float luck = GetModifierTotal(ModifierKind.CoinGainPct) * 100f + _equipmentLuckFlat;
		float clampedOvershield = Mathf.Clamp(_player.Overshield, 0f, Mathf.Max(1f, _player.MaxHp * 0.2f));
		string healthText = _player.ShieldedPieceCount >= 1
			? $"{Mathf.CeilToInt(_player.Hp + clampedOvershield)}/{Mathf.RoundToInt(_player.MaxHp)} (+{Mathf.RoundToInt(clampedOvershield)})"
			: $"{Mathf.CeilToInt(_player.Hp)}/{Mathf.RoundToInt(_player.MaxHp)}";
		string shieldText = _player.ShieldedPieceCount >= 1
			? $"Overshield {Mathf.RoundToInt(clampedOvershield)}"
			: $"Charges {_player.BuffShieldCharges}";

		StringBuilder builder = new();
		builder.AppendLine($"Health: {healthText}");
		builder.AppendLine($"Move Speed: {Mathf.RoundToInt(moveSpeedPct)}%");
		builder.AppendLine($"Shield: {shieldText}");
		builder.AppendLine($"Dodge: {dodgeChance:0.0}%");
		builder.AppendLine($"Crit Chance: {critChance:0.0}%");
		builder.AppendLine($"Crit Damage: {Mathf.RoundToInt(critDamage)}%");
		builder.AppendLine($"Regen: {(regen % 1f > 0.001f ? $"{regen:0.0}" : $"{regen:0}")}/s");
		builder.AppendLine($"Life Steal: {lifeSteal:0.0}%");
		builder.AppendLine($"Range: {Mathf.RoundToInt(range)}");
		builder.Append($"Luck: {Mathf.RoundToInt(luck)}");
		return builder.ToString();
	}

	private string BuildArenaPauseWeaponStatsText()
	{
		StringBuilder builder = new();
		for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			if (slotIndex > 0)
			{
				builder.AppendLine();
			}

			AppendArenaPauseWeaponStats(builder, slotIndex, _equippedLoadout[slotIndex]);
		}

		return builder.ToString().TrimEnd();
	}

	private void AppendArenaPauseWeaponStats(StringBuilder builder, int slotIndex, EquipmentItem? item)
	{
		string slotLabel = GetArenaLoadoutSlotLabel(slotIndex);
		if (item?.Shape == null)
		{
			builder.AppendLine($"{slotLabel}: Empty");
			builder.Append("No equipped weapon in this slot.");
			return;
		}

		ShapeData shape = item.Shape;
		WeaponKind kind = slotIndex == 0 ? _weaponKind : MapEquipmentShapeToWeapon(shape.Key);
		RarityTier rarity = slotIndex == 0 ? _weaponRarity : MapEquipmentRarity(item.Rarity?.Key);
		string rarityLabel = item.Rarity?.DisplayName ?? rarity.ToString();
		int hitCount = Mathf.Max(1, shape.Hits);
		float debugDamageMultiplier = _arenaDebugOverridesActive
			? Mathf.Max(0f, RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.WeaponDamagePercentBonus / 100f, 1f))
			: 1f;
		float debugSizeMultiplier = _arenaDebugOverridesActive
			? Mathf.Max(0.05f, RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.WeaponSizePercent / 100f, 1f))
			: 1f;
		int splitProjectiles = Mathf.FloorToInt(GetItemPower(RunItemId.SplitShot)) + GetModifierFlat(ModifierKind.ExtraSplitShot);
		int debugProjectileBonus = _arenaDebugOverridesActive
			? Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverrides.WeaponProjectileCountFlat))
			: 0;
		int debugRicochetBonus = _arenaDebugOverridesActive
			? Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverrides.WeaponRicochetFlat))
			: 0;
		int debugBounceBonus = _arenaDebugOverridesActive
			? Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverrides.WeaponBounceTimesFlat))
			: 0;

		builder.AppendLine($"{slotLabel}: {shape.DisplayName} | {rarityLabel} | Lv {item.Level}");
		if (item.Level >= EquipmentCatalogDefaults.MaxWeaponLevel)
		{
			builder.AppendLine("Weapon XP: MAX");
		}
		else
		{
			float xpToNextLevel = GetWeaponXpRequiredForNextLevel(item.Level);
			builder.AppendLine($"Weapon XP: {GetRunLoadoutWeaponXp(item):0}/{xpToNextLevel:0}");
		}
		builder.AppendLine($"Attack: {shape.AttackName}");

		if (slotIndex == 0)
		{
			float attacksPerSecond = Mathf.Max(0f, RuntimeMath.FiniteOr(_player.FireRate, 0f));
			float cooldown = attacksPerSecond > 0f ? 1f / attacksPerSecond : 0f;
			float damagePerProjectile = Mathf.Max(0f, RuntimeMath.FiniteOr(_player.BulletDamage, 0f));
			int projectileCount = Mathf.Max(1, 1 + Mathf.Max(0, Mathf.RoundToInt(_equipmentProjectileCountFlat)) + splitProjectiles);
			int ricochet = Mathf.Max(0, Mathf.RoundToInt(_equipmentRicochetFlat) + debugRicochetBonus);
			int bounce = Mathf.Max(0, Mathf.RoundToInt(_equipmentBounceTimesFlat) + debugBounceBonus);
			float critChance = GetTotalCritChancePercent();
			float critDamagePercent = GetTotalCritDamagePercent();
			float expectedCritMultiplier = GetArenaPauseExpectedCritMultiplier(critChance, critDamagePercent);
			float damagePerAttack = damagePerProjectile * projectileCount * hitCount;
			float dps = damagePerAttack * attacksPerSecond * expectedCritMultiplier;
			float sizePercent = Mathf.Max(0f, RuntimeMath.FiniteOr(_bulletRadiusScale, 1f)) * 100f;
			float range = GetCurrentWeaponTargetRange() * RangeDisplayScale;

			builder.AppendLine("Status: Live primary weapon");
			builder.AppendLine($"Damage {FormatArenaPauseNumber(damagePerProjectile)} | APS {FormatArenaPauseNumber(attacksPerSecond)} | DPS {FormatArenaPauseNumber(dps)} | CD {cooldown:0.###}s");
			builder.AppendLine($"Proj {projectileCount} | Hits {hitCount} | Range {Mathf.RoundToInt(range)} | Size {Mathf.RoundToInt(sizePercent)}%");
			builder.Append($"Crit {critChance:0.0}% / {Mathf.RoundToInt(GetArenaPauseDisplayedCritDamagePercent(critDamagePercent))}% | Ric {ricochet} | Bounce {bounce}");
			return;
		}

		float wingCadenceMultiplier = GetWingCadenceMultiplier(item.Modulators);
		float attacksPerSecondSupport = 0f;
		float cooldownSupport = 0f;
		string status = "Standby (install a wing modulator to fire)";
		if (wingCadenceMultiplier > 0f)
		{
			cooldownSupport = GetWingSupportWeaponCooldown(item, kind, wingCadenceMultiplier);
			attacksPerSecondSupport = cooldownSupport > 0f ? 1f / cooldownSupport : 0f;
			status = $"Live wing support ({Mathf.RoundToInt(wingCadenceMultiplier * 100f)}% cadence)";
		}

		float supportDamage = Mathf.Max(0f, StatGenerator.GetEffectiveDamage(item)) * debugDamageMultiplier;
		int supportProjectileCount = Mathf.Max(1, 1 + GetWeaponItemProjectileCount(item) + splitProjectiles + debugProjectileBonus);
		int supportRicochet = Mathf.Max(0, Mathf.RoundToInt(StatGenerator.GetTotalSubstatValue(item, SubstatType.Ricochet)) + debugRicochetBonus);
		int supportBounce = Mathf.Max(0, Mathf.RoundToInt(StatGenerator.GetTotalSubstatValue(item, SubstatType.BounceTimes)) + debugBounceBonus);
		float supportCritChance = Mathf.Clamp(
			GetWeaponItemCritChancePercent(item) + (_arenaDebugOverridesActive ? Mathf.Max(0f, _arenaDebugOverrides.CritChancePercent) : 0f),
			0f,
			100f);
		float supportCritDamagePercent = GetWeaponItemCritDamagePercent(item)
			+ (_arenaDebugOverridesActive ? Mathf.Max(0f, _arenaDebugOverrides.CritDamagePercent) : 0f);
		float supportExpectedCritMultiplier = GetArenaPauseExpectedCritMultiplier(supportCritChance, supportCritDamagePercent);
		float supportDamagePerAttack = supportDamage * supportProjectileCount * hitCount;
		float supportDps = supportDamagePerAttack * attacksPerSecondSupport * supportExpectedCritMultiplier;
		float supportRange = GetWeaponTargetRange(kind, rarity, item) * RangeDisplayScale;
		float supportSizePercent = GetSupportWeaponProjectileSizeScale(item) * debugSizeMultiplier * 100f;
		string apsText = attacksPerSecondSupport > 0f ? FormatArenaPauseNumber(attacksPerSecondSupport) : "--";
		string dpsText = attacksPerSecondSupport > 0f ? FormatArenaPauseNumber(supportDps) : "--";
		string cooldownText = attacksPerSecondSupport > 0f ? $"{cooldownSupport:0.###}s" : "--";

		builder.AppendLine($"Status: {status}");
		builder.AppendLine($"Damage {FormatArenaPauseNumber(supportDamage)} | APS {apsText} | DPS {dpsText} | CD {cooldownText}");
		builder.AppendLine($"Proj {supportProjectileCount} | Hits {hitCount} | Range {Mathf.RoundToInt(supportRange)} | Size {Mathf.RoundToInt(supportSizePercent)}%");
		builder.Append($"Crit {supportCritChance:0.0}% / {Mathf.RoundToInt(GetArenaPauseDisplayedCritDamagePercent(supportCritDamagePercent))}% | Ric {supportRicochet} | Bounce {supportBounce}");
	}

	private static float GetArenaPauseExpectedCritMultiplier(float critChancePercent, float critDamagePercent)
	{
		float critChance = Mathf.Clamp(critChancePercent / 100f, 0f, 1f);
		float critBonus = (BaseCriticalDamagePercent + Mathf.Max(0f, critDamagePercent)) / 100f;
		return 1f + critChance * critBonus;
	}

	private static float GetArenaPauseDisplayedCritDamagePercent(float critDamagePercent)
	{
		return (1f + (BaseCriticalDamagePercent + Mathf.Max(0f, critDamagePercent)) / 100f) * 100f;
	}

	private static string FormatArenaPauseNumber(float value)
	{
		float clamped = Mathf.Max(0f, value);
		if (clamped >= 100f)
		{
			return clamped.ToString("0");
		}

		if (clamped >= 10f)
		{
			return clamped.ToString("0.0");
		}

		return clamped.ToString("0.00");
	}

	private static void StyleArenaPauseButton(Button button, Color fillColor, Color borderColor, Color hoverFillColor, Color hoverBorderColor, Color fontColor)
	{
		button.AddThemeFontSizeOverride("font_size", 14);
		button.AddThemeColorOverride("font_color", fontColor);
		button.AddThemeColorOverride("font_pressed_color", fontColor);
		button.AddThemeColorOverride("font_hover_color", fontColor);

		StyleBoxFlat normal = new()
		{
			BgColor = fillColor,
			BorderColor = borderColor,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
		};
		StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate();
		hover.BgColor = hoverFillColor;
		hover.BorderColor = hoverBorderColor;
		StyleBoxFlat pressed = (StyleBoxFlat)hover.Duplicate();
		pressed.BgColor = fillColor.Darkened(0.12f);

		button.AddThemeStyleboxOverride("normal", normal);
		button.AddThemeStyleboxOverride("hover", hover);
		button.AddThemeStyleboxOverride("pressed", pressed);
	}
}
