using System;
using Godot;

namespace SuperGatchaRebuild.UI;

internal static class InventoryDragPayload
{
    private const string ItemPrefix = "inventory-item:";

    public static string CreateItemPayload(string instanceId)
    {
        return $"{ItemPrefix}{instanceId}";
    }

    public static bool TryGetItemInstanceId(Variant data, out string instanceId)
    {
        instanceId = string.Empty;
        if (data.VariantType != Variant.Type.String)
        {
            return false;
        }

        string payload = data.AsString();
        if (!payload.StartsWith(ItemPrefix, StringComparison.Ordinal) || payload.Length <= ItemPrefix.Length)
        {
            return false;
        }

        instanceId = payload[ItemPrefix.Length..];
        return !string.IsNullOrWhiteSpace(instanceId);
    }
}

internal static class InventoryDragPreview
{
    public static Control Create(Button source, Vector2 atPosition)
    {
        Vector2 previewSize = ResolvePreviewSize(source);
        Vector2 grabOffset = new(
            Mathf.Clamp(atPosition.X, 16f, previewSize.X - 16f),
            Mathf.Clamp(atPosition.Y, 14f, previewSize.Y - 14f));

        PanelContainer frame = new()
        {
            CustomMinimumSize = previewSize,
            Size = previewSize,
            Position = -grabOffset,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0.92f),
            ZIndex = 4096,
            ZAsRelative = false,
        };
        frame.AddThemeStyleboxOverride("panel", CreatePreviewStyle(source));

        MarginContainer margin = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 8);

        Label label = new()
        {
            Text = ResolvePreviewText(source),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", source.GetThemeColor("font_color"));
        label.AddThemeFontSizeOverride("font_size", Math.Max(11, source.GetThemeFontSize("font_size")));

        margin.AddChild(label);
        frame.AddChild(margin);
        return frame;
    }

    private static Vector2 ResolvePreviewSize(Button source)
    {
        float sourceWidth = source.Size.X > 1f ? source.Size.X : source.CustomMinimumSize.X;
        float width = Mathf.Clamp(sourceWidth, 176f, 240f);
        float height = source is InventoryEquippedSlotButton ? 52f : 58f;
        return new Vector2(width, height);
    }

    private static string ResolvePreviewText(Button source)
    {
        string text = source switch
        {
            InventoryItemCardButton itemCard => itemCard.DragLabel,
            InventoryEquippedSlotButton slotButton => slotButton.DragLabel,
            _ => source.Text,
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return "Inventory Item";
        }

        return text.Replace('\n', ' ').Trim();
    }

    private static StyleBox CreatePreviewStyle(Button source)
    {
        StyleBox? baseStyle = source.GetThemeStylebox("normal");
        StyleBox? duplicate = baseStyle?.Duplicate() as StyleBox;
        if (duplicate is StyleBoxFlat flat)
        {
            flat.BgColor = flat.BgColor.Lightened(0.08f);
            flat.BorderColor = flat.BorderColor.Lightened(0.24f);
            flat.BorderWidthLeft = Math.Max(2, flat.BorderWidthLeft);
            flat.BorderWidthTop = Math.Max(2, flat.BorderWidthTop);
            flat.BorderWidthRight = Math.Max(2, flat.BorderWidthRight);
            flat.BorderWidthBottom = Math.Max(2, flat.BorderWidthBottom);
            flat.ShadowColor = flat.BorderColor with { A = Math.Max(flat.ShadowColor.A, 0.40f) };
            flat.ShadowSize = Math.Max(flat.ShadowSize, 14);
            flat.ShadowOffset = new Vector2(4f, 6f);
            return flat;
        }

        if (duplicate != null)
        {
            return duplicate;
        }

        return new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.11f, 0.19f, 0.96f),
            BorderColor = new Color(0.48f, 0.82f, 1f, 0.90f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            ShadowColor = new Color(0f, 0f, 0f, 0.45f),
            ShadowSize = 14,
            ShadowOffset = new Vector2(4f, 6f),
        };
    }
}

public partial class InventoryItemCardButton : Button
{
    public string ItemInstanceId { get; set; } = string.Empty;
    public string DragLabel { get; set; } = "Inventory Item";

    public event Action<string, Vector2>? ContextRequested;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (string.IsNullOrWhiteSpace(ItemInstanceId))
        {
            return default;
        }

        SetDragPreview(InventoryDragPreview.Create(this, atPosition));
        return InventoryDragPayload.CreateItemPayload(ItemInstanceId);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouseButton)
        {
            AcceptEvent();
            ContextRequested?.Invoke(ItemInstanceId, mouseButton.GlobalPosition);
        }
    }
}

public partial class InventoryEquippedSlotButton : Button
{
    public int SlotIndex { get; set; }
    public string? ItemInstanceId { get; set; }
    public string DragLabel { get; set; } = "Equipped Item";

    public event Action<int, string>? ItemDropped;
    public event Action<int, string?, Vector2>? ContextRequested;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (string.IsNullOrWhiteSpace(ItemInstanceId))
        {
            return default;
        }

        SetDragPreview(InventoryDragPreview.Create(this, atPosition));
        return InventoryDragPayload.CreateItemPayload(ItemInstanceId);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        _ = atPosition;
        return InventoryDragPayload.TryGetItemInstanceId(data, out string droppedInstanceId)
            && !string.Equals(droppedInstanceId, ItemInstanceId, StringComparison.Ordinal);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        _ = atPosition;
        if (InventoryDragPayload.TryGetItemInstanceId(data, out string droppedInstanceId))
        {
            ItemDropped?.Invoke(SlotIndex, droppedInstanceId);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouseButton)
        {
            AcceptEvent();
            ContextRequested?.Invoke(SlotIndex, ItemInstanceId, mouseButton.GlobalPosition);
        }
    }
}
