using Godot;
using System;
using System.Text;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild;

public partial class Main
{
	private Control _runModWorkshopOverlay = null!;
	private ItemList _runModWeaponList = null!;
	private ItemList _runModSocketList = null!;
	private ItemList _runModStashList = null!;
	private RichTextLabel _runModDetailLabel = null!;
	private Label _runModFeedbackLabel = null!;
	private Button _runModInstallButton = null!;
	private Button _runModReplaceButton = null!;
	private Button _runModReturnButton = null!;
	private int _runModSelectedWeaponSlot;
	private int _runModSelectedSocketIndex = -1;
	private int _runModSelectedStashIndex = -1;

	private void BuildRunModWorkshopOverlay(Control shell)
	{
		_runModWorkshopOverlay = CreateHudCard(
			Vector2.Zero,
			Vector2.Zero,
			new Color(0.30f, 0.94f, 0.86f, 0.28f),
			new Color(0.01f, 0.05f, 0.08f, 0.97f));
		_runModWorkshopOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_runModWorkshopOverlay.OffsetLeft = 28f;
		_runModWorkshopOverlay.OffsetTop = 28f;
		_runModWorkshopOverlay.OffsetRight = -28f;
		_runModWorkshopOverlay.OffsetBottom = -28f;
		_runModWorkshopOverlay.MouseFilter = Control.MouseFilterEnum.Stop;
		_runModWorkshopOverlay.Visible = false;
		_runModWorkshopOverlay.ZIndex = 4;
		shell.AddChild(_runModWorkshopOverlay);

		Label title = new();
		title.Position = new Vector2(24f, 20f);
		title.Size = new Vector2(360f, 32f);
		title.Text = "MOD WORKSHOP";
		StyleHudLabel(title, 26, new Color(0.91f, 0.98f, 1f, 1f));
		_runModWorkshopOverlay.AddChild(title);

		Label subtitle = new();
		subtitle.Position = new Vector2(24f, 54f);
		subtitle.Size = new Vector2(520f, 18f);
		subtitle.Text = "Socket run-only modulators, swap wings, and return pieces to your run stash.";
		subtitle.AddThemeFontSizeOverride("font_size", 12);
		subtitle.AddThemeColorOverride("font_color", new Color(0.66f, 0.86f, 0.88f, 0.82f));
		_runModWorkshopOverlay.AddChild(subtitle);

		Button closeButton = new();
		closeButton.Position = new Vector2(0f, 18f);
		closeButton.AnchorLeft = 1f;
		closeButton.AnchorRight = 1f;
		closeButton.OffsetLeft = -154f;
		closeButton.OffsetRight = -22f;
		closeButton.Size = new Vector2(132f, 40f);
		closeButton.Text = "Back";
		closeButton.Pressed += CloseRunModWorkshop;
		StyleArenaPauseButton(
			closeButton,
			new Color(0.12f, 0.20f, 0.30f, 0.96f),
			new Color(0.44f, 0.78f, 1f, 0.55f),
			new Color(0.16f, 0.28f, 0.42f, 1f),
			new Color(0.60f, 0.88f, 1f, 0.90f),
			new Color(0.90f, 0.97f, 1f, 1f));
		_runModWorkshopOverlay.AddChild(closeButton);

		HBoxContainer body = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		body.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		body.OffsetLeft = 22f;
		body.OffsetTop = 92f;
		body.OffsetRight = -22f;
		body.OffsetBottom = -22f;
		body.AddThemeConstantOverride("separation", 16);
		_runModWorkshopOverlay.AddChild(body);

		VBoxContainer weaponColumn = BuildRunWorkshopColumn("Loadout", out _runModWeaponList);
		body.AddChild(weaponColumn);

		VBoxContainer socketColumn = BuildRunWorkshopColumn("Sockets", out _runModSocketList);
		body.AddChild(socketColumn);

		VBoxContainer stashColumn = BuildRunWorkshopColumn("Run Stash", out _runModStashList);
		body.AddChild(stashColumn);

		VBoxContainer actionColumn = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(260f, 0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		actionColumn.AddThemeConstantOverride("separation", 10);
		body.AddChild(actionColumn);

		Label detailHeader = new();
		detailHeader.Text = "Details";
		detailHeader.AddThemeFontSizeOverride("font_size", 14);
		detailHeader.AddThemeColorOverride("font_color", new Color(0.72f, 0.88f, 0.97f, 0.86f));
		actionColumn.AddChild(detailHeader);

		_runModDetailLabel = new RichTextLabel
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 280f),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ScrollActive = true,
		};
		_runModDetailLabel.AddThemeFontSizeOverride("normal_font_size", 13);
		_runModDetailLabel.AddThemeColorOverride("default_color", new Color(0.91f, 0.97f, 1f, 0.95f));
		actionColumn.AddChild(_runModDetailLabel);

		_runModInstallButton = new Button();
		_runModInstallButton.Text = "Install To Open Slot";
		_runModInstallButton.Pressed += InstallSelectedRunModulator;
		StyleArenaPauseButton(
			_runModInstallButton,
			new Color(0.08f, 0.30f, 0.22f, 0.96f),
			new Color(0.42f, 1f, 0.82f, 0.48f),
			new Color(0.10f, 0.42f, 0.30f, 1f),
			new Color(0.66f, 1f, 0.90f, 0.88f),
			new Color(0.92f, 1f, 0.97f, 1f));
		actionColumn.AddChild(_runModInstallButton);

		_runModReplaceButton = new Button();
		_runModReplaceButton.Text = "Replace Selected Socket";
		_runModReplaceButton.Pressed += ReplaceSelectedRunModulator;
		StyleArenaPauseButton(
			_runModReplaceButton,
			new Color(0.22f, 0.20f, 0.08f, 0.96f),
			new Color(1f, 0.88f, 0.42f, 0.48f),
			new Color(0.34f, 0.28f, 0.10f, 1f),
			new Color(1f, 0.94f, 0.60f, 0.86f),
			new Color(1f, 0.98f, 0.90f, 1f));
		actionColumn.AddChild(_runModReplaceButton);

		_runModReturnButton = new Button();
		_runModReturnButton.Text = "Return Selected To Stash";
		_runModReturnButton.Pressed += ReturnSelectedRunModulatorToStash;
		StyleArenaPauseButton(
			_runModReturnButton,
			new Color(0.20f, 0.10f, 0.24f, 0.96f),
			new Color(0.86f, 0.54f, 1f, 0.42f),
			new Color(0.30f, 0.14f, 0.36f, 1f),
			new Color(0.94f, 0.72f, 1f, 0.84f),
			new Color(1f, 0.95f, 1f, 1f));
		actionColumn.AddChild(_runModReturnButton);

		_runModFeedbackLabel = new Label();
		_runModFeedbackLabel.CustomMinimumSize = new Vector2(0f, 52f);
		_runModFeedbackLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_runModFeedbackLabel.AddThemeFontSizeOverride("font_size", 12);
		_runModFeedbackLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.90f, 0.92f, 0.82f));
		actionColumn.AddChild(_runModFeedbackLabel);

		_runModWeaponList.ItemSelected += OnRunModWeaponSelected;
		_runModSocketList.ItemSelected += OnRunModSocketSelected;
		_runModStashList.ItemSelected += OnRunModStashSelected;
	}

	private static VBoxContainer BuildRunWorkshopColumn(string title, out ItemList list)
	{
		VBoxContainer column = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(210f, 0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		column.AddThemeConstantOverride("separation", 10);

		Label header = new();
		header.Text = title;
		header.AddThemeFontSizeOverride("font_size", 14);
		header.AddThemeColorOverride("font_color", new Color(0.72f, 0.88f, 0.97f, 0.86f));
		column.AddChild(header);

		list = new ItemList
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 390f),
			SelectMode = ItemList.SelectModeEnum.Single,
			AllowReselect = true,
			FixedColumnWidth = 0,
		};
		column.AddChild(list);
		return column;
	}

	private bool IsRunModWorkshopVisible()
	{
		return IsInstanceValid(_runModWorkshopOverlay) && _runModWorkshopOverlay.Visible;
	}

	private void OpenRunModWorkshop()
	{
		if (!IsInstanceValid(_runModWorkshopOverlay))
		{
			return;
		}

		_runModWorkshopOverlay.Visible = true;
		_runModSelectedWeaponSlot = Mathf.Clamp(_runModSelectedWeaponSlot, 0, _equippedLoadout.Length - 1);
		SetRunModWorkshopFeedback(string.Empty, new Color(0.72f, 0.90f, 0.92f, 0.82f));
		RefreshRunModWorkshopView();
	}

	private void CloseRunModWorkshop()
	{
		if (!IsInstanceValid(_runModWorkshopOverlay))
		{
			return;
		}

		_runModWorkshopOverlay.Visible = false;
		_runModSelectedSocketIndex = -1;
		_runModSelectedStashIndex = -1;
		SetRunModWorkshopFeedback(string.Empty, new Color(0.72f, 0.90f, 0.92f, 0.82f));
	}

	private void RefreshRunModWorkshopView()
	{
		if (!IsRunModWorkshopVisible())
		{
			return;
		}

		RefreshRunModWeaponList();
		RefreshRunModSocketList();
		RefreshRunModStashList();
		RefreshRunModDetailText();
		RefreshRunModButtons();
	}

	private void RefreshRunModWeaponList()
	{
		_runModWeaponList.Clear();

		for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			EquipmentItem? item = GetRunLoadoutItem(slotIndex);
			string text = item == null
				? $"{GetArenaLoadoutSlotLabel(slotIndex)}: Empty"
				: $"{GetArenaLoadoutSlotLabel(slotIndex)}: {item.Shape?.DisplayName ?? "Weapon"} Lv {item.Level} | {item.Modulators.Count}/{item.UnlockedModulatorSlots} mods";
			_runModWeaponList.AddItem(text);
		}

		_runModSelectedWeaponSlot = Mathf.Clamp(_runModSelectedWeaponSlot, 0, Math.Max(0, _equippedLoadout.Length - 1));
		if (_equippedLoadout.Length > 0)
		{
			_runModWeaponList.Select(_runModSelectedWeaponSlot);
		}
	}

	private void RefreshRunModSocketList()
	{
		_runModSocketList.Clear();

		EquipmentItem? item = GetRunLoadoutItem(_runModSelectedWeaponSlot);
		if (item == null)
		{
			_runModSocketList.AddItem("No equipped weapon in this slot.");
			_runModSocketList.SetItemDisabled(0, true);
			_runModSelectedSocketIndex = -1;
			return;
		}

		int socketCount = Math.Max(item.UnlockedModulatorSlots, item.Modulators.Count);
		if (socketCount <= 0)
		{
			_runModSocketList.AddItem("No sockets unlocked yet.");
			_runModSocketList.SetItemDisabled(0, true);
			_runModSelectedSocketIndex = -1;
			return;
		}

		for (int socketIndex = 0; socketIndex < socketCount; socketIndex++)
		{
			string text;
			if (socketIndex < item.Modulators.Count)
			{
				text = $"Socket {socketIndex + 1}: {item.Modulators[socketIndex].DisplayName}";
			}
			else
			{
				text = $"Socket {socketIndex + 1}: Empty";
			}

			_runModSocketList.AddItem(text);
		}

		if (_runModSelectedSocketIndex >= socketCount)
		{
			_runModSelectedSocketIndex = -1;
		}

		if (_runModSelectedSocketIndex >= 0)
		{
			_runModSocketList.Select(_runModSelectedSocketIndex);
		}
	}

	private void RefreshRunModStashList()
	{
		_runModStashList.Clear();

		if (_runStoredModulators.Count == 0)
		{
			_runModStashList.AddItem("No run modulators stashed.");
			_runModStashList.SetItemDisabled(0, true);
			_runModSelectedStashIndex = -1;
			return;
		}

		for (int index = 0; index < _runStoredModulators.Count; index++)
		{
			WeaponModulatorData modulator = _runStoredModulators[index];
			string tierText = modulator.UsesTierPresentation ? $"{modulator.Tier} " : string.Empty;
			string categoryText = WeaponModulatorData.GetCategoryDisplayName(modulator.Category);
			_runModStashList.AddItem($"{tierText}{modulator.DisplayName} | {categoryText}");
		}

		if (_runModSelectedStashIndex >= _runStoredModulators.Count)
		{
			_runModSelectedStashIndex = _runStoredModulators.Count - 1;
		}

		if (_runModSelectedStashIndex >= 0)
		{
			_runModStashList.Select(_runModSelectedStashIndex);
		}
	}

	private void RefreshRunModDetailText()
	{
		StringBuilder builder = new();
		EquipmentItem? item = GetRunLoadoutItem(_runModSelectedWeaponSlot);

		if (item == null)
		{
			builder.Append("Select a slot that has an equipped weapon to manage modulators.");
			_runModDetailLabel.Text = builder.ToString();
			return;
		}

		builder.AppendLine($"[b]{GetArenaLoadoutSlotLabel(_runModSelectedWeaponSlot)}[/b]");
		builder.AppendLine($"{item.Shape?.DisplayName ?? "Weapon"}  |  Lv {item.Level}");
		builder.AppendLine($"Sockets: {item.Modulators.Count}/{item.UnlockedModulatorSlots}");
		builder.AppendLine();

		if (_runModSelectedStashIndex >= 0 && _runModSelectedStashIndex < _runStoredModulators.Count)
		{
			AppendRunModulatorDetails(builder, "Selected stash modulator", _runStoredModulators[_runModSelectedStashIndex]);
		}
		else if (_runModSelectedSocketIndex >= 0 && _runModSelectedSocketIndex < item.Modulators.Count)
		{
			AppendRunModulatorDetails(builder, $"Installed in socket {_runModSelectedSocketIndex + 1}", item.Modulators[_runModSelectedSocketIndex]);
		}
		else
		{
			builder.Append("Select a stash modulator or an installed socket to inspect it here.");
		}

		_runModDetailLabel.Text = builder.ToString();
	}

	private static void AppendRunModulatorDetails(StringBuilder builder, string header, WeaponModulatorData modulator)
	{
		builder.AppendLine($"[b]{header}[/b]");
		builder.AppendLine(modulator.DisplayName);
		builder.AppendLine($"Category: {WeaponModulatorData.GetCategoryDisplayName(modulator.Category)}");
		builder.AppendLine($"Power tier: {modulator.PowerTier}");
		if (!string.IsNullOrWhiteSpace(modulator.Description))
		{
			builder.AppendLine(modulator.Description);
		}

		if (modulator.Substats.Count > 0)
		{
			builder.AppendLine();
			builder.AppendLine("Substats:");
			for (int index = 0; index < modulator.Substats.Count; index++)
			{
				SubstatRoll roll = modulator.Substats[index];
				builder.AppendLine($"- {roll.Type}: {roll.Value:0.##}");
			}
		}
	}

	private void RefreshRunModButtons()
	{
		EquipmentItem? item = GetRunLoadoutItem(_runModSelectedWeaponSlot);
		bool hasWeapon = item != null;
		bool hasSelectedStash = _runModSelectedStashIndex >= 0 && _runModSelectedStashIndex < _runStoredModulators.Count;
		bool hasSelectedInstalled = hasWeapon && _runModSelectedSocketIndex >= 0 && _runModSelectedSocketIndex < item!.Modulators.Count;

		_runModInstallButton.Disabled = !hasWeapon || !hasSelectedStash || !CanInstallRunStoredModulator(_runModSelectedWeaponSlot, _runModSelectedStashIndex);
		_runModReplaceButton.Disabled = !hasSelectedInstalled || !hasSelectedStash || !CanInstallRunStoredModulator(_runModSelectedWeaponSlot, _runModSelectedStashIndex, _runModSelectedSocketIndex);
		_runModReturnButton.Disabled = !hasSelectedInstalled;
	}

	private void InstallSelectedRunModulator()
	{
		if (_runModSelectedStashIndex < 0 || !TryInstallRunStoredModulator(_runModSelectedWeaponSlot, _runModSelectedStashIndex))
		{
			SetRunModWorkshopFeedback("That modulator cannot be installed into the selected weapon right now.", new Color(1f, 0.72f, 0.64f, 0.92f));
			RefreshRunModButtons();
			return;
		}

		EquipmentItem? item = GetRunLoadoutItem(_runModSelectedWeaponSlot);
		_runModSelectedSocketIndex = item == null ? -1 : item.Modulators.Count - 1;
		_runModSelectedStashIndex = Mathf.Clamp(_runModSelectedStashIndex, -1, _runStoredModulators.Count - 1);
		SyncRunLoadoutAfterWorkshopChange();
		ShowStatusBanner($"Installed modulator on {GetArenaLoadoutSlotLabel(_runModSelectedWeaponSlot)}");
		SetRunModWorkshopFeedback("Installed the selected modulator into the next open socket.", new Color(0.74f, 1f, 0.86f, 0.92f));
	}

	private void ReplaceSelectedRunModulator()
	{
		if (_runModSelectedSocketIndex < 0 || _runModSelectedStashIndex < 0
			|| !TryReplaceRunStoredModulator(_runModSelectedWeaponSlot, _runModSelectedSocketIndex, _runModSelectedStashIndex))
		{
			SetRunModWorkshopFeedback("That replacement is not valid for the selected socket.", new Color(1f, 0.72f, 0.64f, 0.92f));
			RefreshRunModButtons();
			return;
		}

		_runModSelectedStashIndex = Mathf.Clamp(_runModSelectedStashIndex, -1, _runStoredModulators.Count - 1);
		SyncRunLoadoutAfterWorkshopChange();
		ShowStatusBanner($"Replaced a modulator on {GetArenaLoadoutSlotLabel(_runModSelectedWeaponSlot)}");
		SetRunModWorkshopFeedback("Replaced the selected socket with the chosen stash modulator.", new Color(0.74f, 1f, 0.86f, 0.92f));
	}

	private void ReturnSelectedRunModulatorToStash()
	{
		if (_runModSelectedSocketIndex < 0 || !TryReturnRunModulatorToStash(_runModSelectedWeaponSlot, _runModSelectedSocketIndex))
		{
			SetRunModWorkshopFeedback("Select an installed modulator to return it to the stash.", new Color(1f, 0.72f, 0.64f, 0.92f));
			RefreshRunModButtons();
			return;
		}

		_runModSelectedSocketIndex = -1;
		_runModSelectedStashIndex = _runStoredModulators.Count - 1;
		SyncRunLoadoutAfterWorkshopChange();
		ShowStatusBanner($"Returned a modulator from {GetArenaLoadoutSlotLabel(_runModSelectedWeaponSlot)}");
		SetRunModWorkshopFeedback("Returned the selected modulator to the run stash.", new Color(0.74f, 1f, 0.86f, 0.92f));
	}

	private void SyncRunLoadoutAfterWorkshopChange()
	{
		RefreshEquipmentLoadout();
		ApplyEquippedPrimaryToRun();
		ApplyDerivedPlayerStats(false);
		if (_merchantEncounterActive)
		{
			RefreshMerchantOfferPreviews();
		}
		UpdateArenaPauseOverlayState();
		RefreshRunModWorkshopView();
	}

	private void OnRunModWeaponSelected(long index)
	{
		_runModSelectedWeaponSlot = (int)index;
		_runModSelectedSocketIndex = -1;
		RefreshRunModSocketList();
		RefreshRunModDetailText();
		RefreshRunModButtons();
	}

	private void OnRunModSocketSelected(long index)
	{
		_runModSelectedSocketIndex = (int)index;
		RefreshRunModDetailText();
		RefreshRunModButtons();
	}

	private void OnRunModStashSelected(long index)
	{
		if (_runStoredModulators.Count == 0)
		{
			_runModSelectedStashIndex = -1;
			return;
		}

		_runModSelectedStashIndex = Mathf.Clamp((int)index, 0, _runStoredModulators.Count - 1);
		RefreshRunModDetailText();
		RefreshRunModButtons();
	}

	private void SetRunModWorkshopFeedback(string text, Color color)
	{
		if (!IsInstanceValid(_runModFeedbackLabel))
		{
			return;
		}

		_runModFeedbackLabel.Text = text;
		_runModFeedbackLabel.AddThemeColorOverride("font_color", color);
	}
}