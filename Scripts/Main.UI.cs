using Godot;
using SuperGatchaRebuild.DebugTools;

namespace SuperGatchaRebuild;

public partial class Main
{
	private void BuildHud()
	{
		_hud = new CanvasLayer();
		AddChild(_hud);

		Control root = new();
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.MouseFilter = Control.MouseFilterEnum.Pass;
		_hud.AddChild(root);

		const float topY = 10f;
		const float gap = 8f;
		const float topCardH = 40f;

		Control leftGroup = new()
		{
			Position = new Vector2(16f, topY),
			Size = new Vector2(360f, topCardH),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		root.AddChild(leftGroup);

		float leftX = 0f;
		Control hpCard = CreateHudStatCard(new Vector2(leftX, 0f), new Vector2(122f, topCardH), "HP", out _hpLabel, 14);
		leftGroup.AddChild(hpCard);

		_hpBarBack = new ColorRect();
		_hpBarBack.Position = new Vector2(10f, 31f);
		_hpBarBack.Size = new Vector2(102f, 6f);
		_hpBarBack.Color = new Color(1f, 1f, 1f, 0.10f);
		hpCard.AddChild(_hpBarBack);

		_hpBarFill = new ColorRect();
		_hpBarFill.Position = _hpBarBack.Position;
		_hpBarFill.Size = _hpBarBack.Size;
		_hpBarFill.Color = new Color(0.20f, 0.84f, 0.40f, 1f);
		hpCard.AddChild(_hpBarFill);

		_hpOvershieldFill = new ColorRect();
		_hpOvershieldFill.Position = _hpBarBack.Position;
		_hpOvershieldFill.Size = Vector2.Zero;
		_hpOvershieldFill.Color = new Color(0.00f, 0.81f, 1f, 0.95f);
		_hpOvershieldFill.Visible = false;
		hpCard.AddChild(_hpOvershieldFill);

		leftX += 122f + gap;
		Control waveCard = CreateHudStatCard(new Vector2(leftX, 0f), new Vector2(54f, topCardH), "Wave", out _waveLabel, 14);
		leftGroup.AddChild(waveCard);

		leftX += 54f + gap;
		Control shrineCard = CreateHudCard(
			new Vector2(leftX, 6f),
			new Vector2(84f, 28f),
			new Color(0.20f, 0.83f, 0.60f, 0.38f),
			new Color(0.04f, 0.18f, 0.14f, 0.44f));
		leftGroup.AddChild(shrineCard);

		Label shrineIcon = new();
		shrineIcon.Position = new Vector2(6f, 5f);
		shrineIcon.Size = new Vector2(16f, 16f);
		shrineIcon.Text = "📊";
		shrineIcon.AddThemeFontSizeOverride("font_size", 12);
		shrineCard.AddChild(shrineIcon);

		_shrineLeftLabel = new Label();
		_shrineLeftLabel.Position = new Vector2(24f, 4f);
		_shrineLeftLabel.Size = new Vector2(18f, 16f);
		_shrineLeftLabel.AddThemeFontSizeOverride("font_size", 13);
		_shrineLeftLabel.AddThemeColorOverride("font_color", new Color(0.24f, 0.95f, 0.70f, 1f));
		_shrineLeftLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.90f));
		_shrineLeftLabel.AddThemeConstantOverride("outline_size", 1);
		shrineCard.AddChild(_shrineLeftLabel);

		Label shrineText = new();
		shrineText.Position = new Vector2(44f, 8f);
		shrineText.Size = new Vector2(34f, 14f);
		shrineText.Text = "LEFT";
		shrineText.AddThemeFontSizeOverride("font_size", 9);
		shrineText.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.55f));
		shrineCard.AddChild(shrineText);

		Control centerGroup = new();
		centerGroup.AnchorLeft = 0.5f;
		centerGroup.AnchorRight = 0.5f;
		centerGroup.AnchorTop = 0f;
		centerGroup.AnchorBottom = 0f;
		centerGroup.OffsetLeft = -133f;
		centerGroup.OffsetRight = 133f;
		centerGroup.OffsetTop = topY;
		centerGroup.OffsetBottom = topY + topCardH;
		centerGroup.MouseFilter = Control.MouseFilterEnum.Ignore;
		root.AddChild(centerGroup);

		float centerX = 0f;
		Control killCard = CreateHudStatCard(new Vector2(centerX, 0f), new Vector2(58f, topCardH), "Kills", out _killsLabel, 14);
		centerGroup.AddChild(killCard);

		centerX += 58f + gap;
		Control timeCard = CreateHudStatCard(new Vector2(centerX, 0f), new Vector2(58f, topCardH), "Time", out _timeLabel, 14);
		centerGroup.AddChild(timeCard);

		centerX += 58f + gap;
		Control xpCard = CreateHudStatCard(new Vector2(centerX, 0f), new Vector2(52f, topCardH), "XP", out _xpLabel, 14);
		centerGroup.AddChild(xpCard);

		centerX += 52f + gap;
		Control coinCard = CreateHudCard(
			new Vector2(centerX, 0f),
			new Vector2(74f, topCardH),
			new Color(1f, 1f, 1f, 0.15f),
			new Color(0f, 0f, 0f, 0.75f));
		centerGroup.AddChild(coinCard);

		Label coinIcon = new();
		coinIcon.Position = new Vector2(10f, 11f);
		coinIcon.Size = new Vector2(18f, 16f);
		coinIcon.Text = "🪙";
		coinIcon.AddThemeFontSizeOverride("font_size", 13);
		coinIcon.AddThemeColorOverride("font_color", new Color(0.98f, 0.78f, 0.20f, 1f));
		coinCard.AddChild(coinIcon);

		_coinsLabel = new Label();
		_coinsLabel.Position = new Vector2(30f, 9f);
		_coinsLabel.Size = new Vector2(36f, 20f);
		_coinsLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_coinsLabel.AddThemeFontSizeOverride("font_size", 15);
		_coinsLabel.AddThemeColorOverride("font_color", new Color(0.98f, 0.84f, 0.28f, 1f));
		_coinsLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.90f));
		_coinsLabel.AddThemeConstantOverride("outline_size", 1);
		coinCard.AddChild(_coinsLabel);

		_itemSummaryLabel = new Label();
		_itemSummaryLabel.Position = new Vector2(16f, 58f);
		_itemSummaryLabel.Size = new Vector2(900f, 20f);
		_itemSummaryLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		_itemSummaryLabel.AddThemeFontSizeOverride("font_size", 11);
		_itemSummaryLabel.AddThemeColorOverride("font_color", new Color(0.76f, 0.84f, 0.96f, 0.88f));
		_itemSummaryLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.82f));
		_itemSummaryLabel.AddThemeConstantOverride("outline_size", 1);
		root.AddChild(_itemSummaryLabel);

		_debugWeaponTimingLabel = new Label();
		_debugWeaponTimingLabel.Position = new Vector2(16f, 80f);
		_debugWeaponTimingLabel.Size = new Vector2(760f, 36f);
		_debugWeaponTimingLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_debugWeaponTimingLabel.Visible = false;
		_debugWeaponTimingLabel.AddThemeFontSizeOverride("font_size", 11);
		_debugWeaponTimingLabel.AddThemeColorOverride("font_color", new Color(0.98f, 0.90f, 0.55f, 0.96f));
		_debugWeaponTimingLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.88f));
		_debugWeaponTimingLabel.AddThemeConstantOverride("outline_size", 1);
		root.AddChild(_debugWeaponTimingLabel);

		Control bossPanel = CreateHudCard(
			new Vector2(0f, 48f),
			new Vector2(500f, 58f),
			new Color(0.66f, 0.38f, 0.92f, 0.42f),
			new Color(0f, 0f, 0f, 0.70f));
		bossPanel.AnchorLeft = 0.5f;
		bossPanel.AnchorRight = 0.5f;
		bossPanel.OffsetLeft = -250f;
		bossPanel.OffsetRight = 250f;
		bossPanel.OffsetTop = 48f;
		bossPanel.OffsetBottom = 106f;
		root.AddChild(bossPanel);

		_bossLabel = new Label();
		_bossLabel.Position = new Vector2(0f, 3f);
		_bossLabel.Size = new Vector2(500f, 20f);
		_bossLabel.HorizontalAlignment = HorizontalAlignment.Center;
		StyleHudLabel(_bossLabel, 18, new Color(0.90f, 0.74f, 1f, 1f));
		_bossLabel.Text = "";
		bossPanel.AddChild(_bossLabel);

		_bossHpBack = new ColorRect();
		_bossHpBack.Color = new Color(0f, 0f, 0f, 0.70f);
		_bossHpBack.Position = new Vector2(8f, 28f);
		_bossHpBack.Size = new Vector2(484f, 16f);
		bossPanel.AddChild(_bossHpBack);

		_bossHpFill = new ColorRect();
		_bossHpFill.Color = new Color(0.66f, 0.32f, 0.93f, 1f);
		_bossHpFill.Position = _bossHpBack.Position;
		_bossHpFill.Size = _bossHpBack.Size;
		bossPanel.AddChild(_bossHpFill);

		_bossPctLabel = new Label();
		_bossPctLabel.Position = new Vector2(8f, 26f);
		_bossPctLabel.Size = new Vector2(484f, 18f);
		_bossPctLabel.HorizontalAlignment = HorizontalAlignment.Center;
		StyleHudLabel(_bossPctLabel, 11, Colors.White);
		bossPanel.AddChild(_bossPctLabel);

		_waypointRoot = new Control();
		_waypointRoot.Visible = false;
		_waypointRoot.Size = new Vector2(44f, 44f);
		root.AddChild(_waypointRoot);

		_waypointArrow = new Polygon2D();
		_waypointArrow.Polygon = new Vector2[]
		{
			new(0f, -12f),
			new(11f, 10f),
			new(-11f, 10f),
		};
		_waypointArrow.Color = new Color(0.75f, 0.52f, 0.98f, 1f);
		_waypointArrow.Position = Vector2.Zero;
		_waypointRoot.AddChild(_waypointArrow);

		_waypointText = new Label();
		_waypointText.Position = new Vector2(-20f, 12f);
		_waypointText.Size = new Vector2(72f, 20f);
		_waypointText.HorizontalAlignment = HorizontalAlignment.Center;
		_waypointText.AddThemeFontSizeOverride("font_size", 11);
		_waypointText.Text = "BOSS";
		_waypointRoot.AddChild(_waypointText);

		_levelUpPanel = new Panel();
		_levelUpPanel.Size = new Vector2(460f, 250f);
		_levelUpPanel.Position = new Vector2(570f, 320f);
		_levelUpPanel.MouseFilter = Control.MouseFilterEnum.Stop;
		_levelUpPanel.ZIndex = 80;
		_levelUpPanel.Visible = false;
		root.AddChild(_levelUpPanel);

		_waveBanner = new Label();
		_waveBanner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_waveBanner.HorizontalAlignment = HorizontalAlignment.Center;
		_waveBanner.VerticalAlignment = VerticalAlignment.Center;
		_waveBanner.Visible = false;
		_waveBanner.ZIndex = 40;
		_waveBanner.MouseFilter = Control.MouseFilterEnum.Ignore;
		StyleHudLabel(_waveBanner, 56, new Color(1f, 0.96f, 0.78f, 1f));
		root.AddChild(_waveBanner);
		_waveBanner.PivotOffset = new Vector2(800f, 450f);

		_hurtFlashOverlay = new ColorRect();
		_hurtFlashOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_hurtFlashOverlay.Color = new Color(0.85f, 0.06f, 0.08f, 0f);
		_hurtFlashOverlay.Material = CreateRadialOverlayMaterial(0.30f);
		_hurtFlashOverlay.ZIndex = 45;
		_hurtFlashOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		root.AddChild(_hurtFlashOverlay);

		_lowHpOverlay = new ColorRect();
		_lowHpOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_lowHpOverlay.Color = new Color(0.20f, 0.02f, 0.02f, 0f);
		_lowHpOverlay.Material = CreateRadialOverlayMaterial(0.54f);
		_lowHpOverlay.ZIndex = 44;
		_lowHpOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		root.AddChild(_lowHpOverlay);

		_levelUpTitle = new Label();
		_levelUpTitle.Position = new Vector2(18f, 14f);
		_levelUpTitle.Size = new Vector2(424f, 36f);
		_levelUpTitle.HorizontalAlignment = HorizontalAlignment.Center;
		_levelUpTitle.AddThemeFontSizeOverride("font_size", 24);
		_levelUpTitle.Text = "LEVEL UP - CHOOSE ITEM";
		_levelUpPanel.AddChild(_levelUpTitle);

		_itemBtn0 = new Button();
		_itemBtn0.Position = new Vector2(20f, 66f);
		_itemBtn0.Size = new Vector2(420f, 48f);
		_itemBtn0.MouseFilter = Control.MouseFilterEnum.Stop;
		_itemBtn0.FocusMode = Control.FocusModeEnum.All;
		_itemBtn0.Pressed += () => SelectLevelItem(0);
		_levelUpPanel.AddChild(_itemBtn0);

		_itemBtn1 = new Button();
		_itemBtn1.Position = new Vector2(20f, 124f);
		_itemBtn1.Size = new Vector2(420f, 48f);
		_itemBtn1.MouseFilter = Control.MouseFilterEnum.Stop;
		_itemBtn1.FocusMode = Control.FocusModeEnum.All;
		_itemBtn1.Pressed += () => SelectLevelItem(1);
		_levelUpPanel.AddChild(_itemBtn1);

		_itemBtn2 = new Button();
		_itemBtn2.Position = new Vector2(20f, 182f);
		_itemBtn2.Size = new Vector2(420f, 48f);
		_itemBtn2.MouseFilter = Control.MouseFilterEnum.Stop;
		_itemBtn2.FocusMode = Control.FocusModeEnum.All;
		_itemBtn2.Pressed += () => SelectLevelItem(2);
		_levelUpPanel.AddChild(_itemBtn2);

		BuildArenaMerchantInspectOverlay(root);
		BuildArenaPauseOverlay(root);
	}

	private static Control CreateHudCard(Vector2 position, Vector2 size, Color borderColor, Color fillColor)
	{
		Control card = new()
		{
			Position = position,
			Size = size,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};

		Panel panel = new();
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		StyleBoxFlat sb = new()
		{
			BgColor = fillColor,
			BorderColor = borderColor,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
		};
		panel.AddThemeStyleboxOverride("panel", sb);
		card.AddChild(panel);

		return card;
	}

	private Control CreateHudStatCard(Vector2 position, Vector2 size, string header, out Label valueLabel, int valueFontSize)
	{
		Control card = CreateHudCard(position, size, new Color(1f, 1f, 1f, 0.15f), new Color(0f, 0f, 0f, 0.74f));

		Label h = new();
		h.Position = new Vector2(8f, 3f);
		h.Size = new Vector2(size.X - 16f, 12f);
		h.Text = header.ToUpperInvariant();
		h.AddThemeFontSizeOverride("font_size", 9);
		h.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.52f));
		card.AddChild(h);

		valueLabel = new Label();
		valueLabel.Position = new Vector2(8f, 13f);
		valueLabel.Size = new Vector2(size.X - 16f, 18f);
		valueLabel.AddThemeFontSizeOverride("font_size", valueFontSize);
		valueLabel.AddThemeColorOverride("font_color", new Color(0.98f, 0.99f, 1f, 1f));
		valueLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.90f));
		valueLabel.AddThemeConstantOverride("outline_size", 1);
		card.AddChild(valueLabel);

		return card;
	}

	private static Label AddStatRow(Control parent, float y, string icon, string label)
	{
		Label left = new();
		left.Position = new Vector2(10f, y);
		left.Size = new Vector2(102f, 16f);
		left.Text = $"{icon} {label}";
		left.AddThemeFontSizeOverride("font_size", 11);
		left.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.58f));
		parent.AddChild(left);

		Label right = new();
		right.Position = new Vector2(110f, y);
		right.Size = new Vector2(56f, 16f);
		right.HorizontalAlignment = HorizontalAlignment.Right;
		right.AddThemeFontSizeOverride("font_size", 11);
		right.AddThemeColorOverride("font_color", Colors.White);
		right.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.88f));
		right.AddThemeConstantOverride("outline_size", 1);
		parent.AddChild(right);

		return right;
	}

	private static void AddStatPanelDivider(Control parent, float y, float width)
	{
		ColorRect divider = new();
		divider.Position = new Vector2(10f, y);
		divider.Size = new Vector2(width, 1f);
		divider.Color = new Color(1f, 1f, 1f, 0.08f);
		parent.AddChild(divider);
	}

	private static void SetStatRowValue(Label label, string value, bool boosted)
	{
		label.Text = value;
		label.AddThemeColorOverride("font_color", boosted ? new Color(0.20f, 0.93f, 0.62f, 1f) : Colors.White);
	}

	private static void StyleHudLabel(Label label, int fontSize, Color color)
	{
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.92f));
		label.AddThemeConstantOverride("outline_size", 2);
	}

	private static string FormatArenaTime(float seconds)
	{
		int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
		int m = s / 60;
		int rem = s % 60;
		return $"{m}:{rem:00}";
	}

	private static ShaderMaterial CreateRadialOverlayMaterial(float innerRadius)
	{
		Shader shader = new();
		shader.Code = @"
shader_type canvas_item;
uniform float inner_radius = 0.35;

void fragment() {
    vec2 p = UV * 2.0 - vec2(1.0);
    float d = length(p);
    float edge = smoothstep(inner_radius, 1.0, d);
    COLOR = vec4(COLOR.rgb, COLOR.a * edge);
}";

		ShaderMaterial mat = new();
		mat.Shader = shader;
		mat.SetShaderParameter("inner_radius", innerRadius);
		return mat;
	}

	private void OnExitArenaPressed()
	{
		DebugCommandService? debugCommands = GetNodeOrNull("/root/DebugCommandService") as DebugCommandService;
		if (debugCommands != null && GodotObject.IsInstanceValid(debugCommands))
		{
			debugCommands.ReturnToHub(HubScenePath);
			return;
		}

		if (string.IsNullOrWhiteSpace(HubScenePath) || !FileAccess.FileExists(HubScenePath))
		{
			return;
		}

		PersistRunLoadoutWeaponProgress();
		_ = GetTree().ChangeSceneToFile(HubScenePath);
	}

	private void ShowWaveBanner(int wave)
	{
		if (_waveBanner == null)
		{
			return;
		}

		_waveBanner.Text = wave % BossWaveInterval == 0 ? $"WAVE {wave}  BOSS INBOUND" : $"WAVE {wave}";
		_waveBanner.Visible = true;
		_waveBannerTimer = 1.25f;
		_waveBanner.Scale = Vector2.One * 0.82f;
		_waveBanner.Modulate = new Color(1f, 0.96f, 0.80f, 0f);
	}

	private void ShowStatusBanner(string text)
	{
		if (_waveBanner == null)
		{
			return;
		}

		_waveBanner.Text = text;
		_waveBanner.Visible = true;
		_waveBannerTimer = 1.25f;
		_waveBanner.Scale = Vector2.One * 0.82f;
		_waveBanner.Modulate = new Color(1f, 0.96f, 0.80f, 0f);
	}

	private void UpdateHud()
	{
		float hpPct = Mathf.Clamp(_player.Hp / Mathf.Max(1f, _player.MaxHp), 0f, 1f);
		float hpBarW = _hpBarBack.Size.X;
		_hpBarFill.Size = new Vector2(hpBarW * hpPct, _hpBarBack.Size.Y);

		bool showOvershield = _player.ShieldedPieceCount >= 1;
		_hpOvershieldFill.Visible = showOvershield;
		if (showOvershield)
		{
			float overshieldCap = Mathf.Max(1f, _player.MaxHp * 0.2f);
			float clampedOvershield = Mathf.Clamp(_player.Overshield, 0f, overshieldCap);
			float overPct = clampedOvershield / Mathf.Max(1f, _player.MaxHp);
			_hpOvershieldFill.Size = new Vector2(hpBarW * overPct, _hpBarBack.Size.Y);
			_hpLabel.Text = $"{Mathf.CeilToInt(_player.Hp + clampedOvershield)}/{Mathf.RoundToInt(_player.MaxHp)}";
		}
		else
		{
			_hpOvershieldFill.Size = Vector2.Zero;
			_hpLabel.Text = $"{Mathf.CeilToInt(_player.Hp)}/{Mathf.RoundToInt(_player.MaxHp)}";
		}

		_waveLabel.Text = _waveNum.ToString();
		if (IsInstanceValid(_shrineLeftLabel))
		{
			_shrineLeftLabel.Text = Mathf.Max(0, _waveLeft).ToString();
		}

		_killsLabel.Text = _kills.ToString();
		if (IsInstanceValid(_timeLabel))
		{
			_timeLabel.Text = FormatArenaTime(_gameTimer);
		}

		_xpLabel.Text = _xp.ToString("0");
		_coinsLabel.Text = _coins.ToString();
		_itemSummaryLabel.Text = BuildItemSummaryText();

		UpdateDebugWeaponTimingLabel();
		UpdateArenaPauseOverlayState();

		_levelUpTitle.Text = _pauseForItemChoice
			? (_pendingItemChoices > 1 ? $"LEVEL UP - CHOOSE ITEM ({_pendingItemChoices} LEFT)" : "LEVEL UP - CHOOSE ITEM")
			: "LEVEL UP - CHOOSE ITEM";

		bool bossActive = _boss != null && _boss.IsAlive;
		_bossLabel.Visible = bossActive;
		_bossHpBack.Visible = bossActive;
		_bossHpFill.Visible = bossActive;
		_bossPctLabel.Visible = bossActive;

		if (bossActive && _boss != null)
		{
			float pct = Mathf.Clamp(_boss.Hp / Mathf.Max(1f, _boss.MaxHp), 0f, 1f);
			_bossHpFill.Size = new Vector2(_bossHpBack.Size.X * pct, _bossHpBack.Size.Y);
			_bossPctLabel.Text = $"{pct * 100f:0}%";
			Color baseColor = GetBossHudColor(_boss.Kind);
			float shimmer = 0.90f + 0.10f * Mathf.Sin(_gameTimer * 7f);
			_bossHpFill.Color = new Color(baseColor.R * shimmer, baseColor.G * shimmer, baseColor.B * shimmer, 1f);
		}
	}

	private void UpdateDebugWeaponTimingLabel()
	{
		if (!IsInstanceValid(_debugWeaponTimingLabel))
		{
			return;
		}

		bool show = _debugPanelVisible;
		_debugWeaponTimingLabel.Visible = show;
		if (!show)
		{
			return;
		}

		float weaponCooldown = Mathf.Max(0.05f, RuntimeMath.FiniteOr(_weaponCooldownSeconds, GetLegacyWeaponCooldown(_weaponKind)));
		float effectiveFireRate = Mathf.Max(0.05f, RuntimeMath.FiniteOr(_player.FireRate, 0f));
		float effectiveCooldown = effectiveFireRate > 0f ? 1f / effectiveFireRate : 0f;

		float bootsPower = GetItemPower(RunItemId.RunnerBoots);
		float syringePower = GetItemPower(RunItemId.SoldierSyringe);
		float whetPower = GetItemPower(RunItemId.Whetstone);
		float vitalPower = GetItemPower(RunItemId.VitalCore);

		float moveMult = 1f + bootsPower * 0.10f;
		float fireMult = 1f + syringePower * 0.18f;
		float dmgMult = 1f + whetPower * 0.20f;
		float hpMult = 1f + vitalPower * 0.18f;

		float shapeTier = GetRarityMultiplier(_shapeRarity);
		float weaponTier = GetRarityMultiplier(_weaponRarity);
		float profileFireMultiplier = GetWeaponBaseFireProfileMultiplier(_weaponKind);
		float rarityFireMultiplier = GetWeaponFireTierMultiplier(weaponTier);
		ApplyShapeProfile(ref moveMult, ref dmgMult, ref hpMult, shapeTier);
		ApplyWeaponProfile(ref fireMult, ref dmgMult, ref moveMult, weaponTier);

		float modFire = GetModifierTotal(ModifierKind.FireRatePct);
		float equipmentAttackMultiplier = 1f + _equipmentAttackSpeedPct / 100f;
		fireMult *= 1f + modFire;
		fireMult *= equipmentAttackMultiplier;

		float debugAttackMultiplier = 1f;
		if (_arenaDebugOverridesActive)
		{
			debugAttackMultiplier = 1f + _arenaDebugOverrides.WeaponAttackSpeedPercent / 100f;
		}

		float displayedFireMultiplier = fireMult * debugAttackMultiplier;
		_debugWeaponTimingLabel.Text =
			$"DEBUG FIRE  |  Weapon {_weaponKind} {_weaponRarity}  |  Base CD {weaponCooldown:0.###}s  |  Effective CD {effectiveCooldown:0.###}s  |  FireRate x{displayedFireMultiplier:0.###}\n" +
			$"Breakdown: profile x{profileFireMultiplier:0.###}, rarity x{rarityFireMultiplier:0.###}, syringe x{(1f + syringePower * 0.18f):0.###}, mod x{(1f + modFire):0.###}, equip x{equipmentAttackMultiplier:0.###}, debug x{debugAttackMultiplier:0.###}";
	}

	private void UpdateBossWaypoint()
	{
		if (_boss != null && _boss.IsAlive)
		{
			float pct = _boss.Hp / Mathf.Max(1f, _boss.MaxHp) * 100f;
			Color c = GetBossWaypointColor(_boss.Kind);
			UpdateWaypointTarget(_boss.Position, 1.2f, c, $"{Mathf.Ceil(pct)}%");
			return;
		}

		if (TryGetMerchantWaypointTarget(out Vector2 merchantTarget, out Color merchantColor, out string merchantText))
		{
			UpdateWaypointTarget(merchantTarget, 6f, merchantColor, merchantText);
			return;
		}

		if (_challengePillar != null && IsInstanceValid(_challengePillar))
		{
			Color c = _challengePillar.IsActivated
				? new Color(1f, 0.62f, 0.24f, 1f)
				: new Color(0.24f, 1f, 0.76f, 1f);
			string text = _challengePillar.IsActivated ? "CLEAR" : "PILLAR";
			UpdateWaypointTarget(_challengePillar.Position, 6f, c, text);
			return;
		}

		_waypointRoot.Visible = false;
	}

	private void UpdateWaypointTarget(Vector2 targetPosition, float targetHeight, Color color, string text)
	{
		if (!IsInstanceValid(_camera))
		{
			_waypointRoot.Visible = false;
			return;
		}

		Vector2 screenSize = GetViewport().GetVisibleRect().Size;
		Vector3 worldPos = new Vector3(targetPosition.X, targetHeight, targetPosition.Y);
		bool behind = _camera.IsPositionBehind(worldPos);
		Vector2 screenPos = _camera.UnprojectPosition(worldPos);

		_waypointArrow.Color = color;
		_waypointText.Modulate = color;

		float margin = 30f;
		bool onScreen = !behind && screenPos.X >= 0f && screenPos.X <= screenSize.X && screenPos.Y >= 0f && screenPos.Y <= screenSize.Y;

		_waypointRoot.Visible = true;

		if (onScreen)
		{
			_waypointRoot.Position = new Vector2(screenPos.X, screenPos.Y - 34f);
			_waypointRoot.Rotation = Mathf.Pi;
		}
		else
		{
			Vector2 center = screenSize * 0.5f;
			Vector2 d = screenPos - center;
			if (behind)
			{
				d = -d;
			}
			if (d.LengthSquared() < 0.001f)
			{
				d = Vector2.Right;
			}

			float t = 1f;
			if (Mathf.Abs(d.X) > 0.001f)
			{
				float tx = (d.X > 0f ? (screenSize.X - margin - center.X) : (margin - center.X)) / d.X;
				t = Mathf.Min(t, tx);
			}
			if (Mathf.Abs(d.Y) > 0.001f)
			{
				float ty = (d.Y > 0f ? (screenSize.Y - margin - center.Y) : (margin - center.Y)) / d.Y;
				t = Mathf.Min(t, ty);
			}

			Vector2 edge = center + d * t;
			edge.X = Mathf.Clamp(edge.X, margin, screenSize.X - margin);
			edge.Y = Mathf.Clamp(edge.Y, margin, screenSize.Y - margin);

			_waypointRoot.Position = edge;
			_waypointRoot.Rotation = Mathf.Atan2(d.Y, d.X) + Mathf.Pi * 0.5f;
		}

		_waypointText.Text = text;
	}
}