using System;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

internal static class WorkbenchInputFixture
{
    public static async Task Click(Node host, Control target)
    {
        if (!target.IsVisibleInTree()) throw new InvalidOperationException("Workbench click requires a visible control: " + target.Name);
        if (DisplayServer.GetName() == "headless")
        {
            ((BaseButton)target).EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(host, 2);
            return;
        }
        Vector2 position = target.GetGlobalRect().GetCenter();
        Input.WarpMouse(position);
        Motion(host, position, Vector2.Zero, false);
        await Frames(host, 2);
        Dispatch(host, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = position, GlobalPosition = position });
        await Frames(host, 2);
        Dispatch(host, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position, GlobalPosition = position });
        await Frames(host, 2);
    }

    // Real viewport GUI drag dispatch; source/target rectangles follow the actual layout.
    public static async Task Drag(Node host, Control source, Control target, bool leaveHeld = false, Func<string>? diagnosticContext = null)
    {
        if (!source.IsVisibleInTree() || !target.IsVisibleInTree())
            throw new InvalidOperationException("Workbench drag requires visible source and target controls");
        Vector2 start = source.GetGlobalRect().GetCenter();
        Input.WarpMouse(start);
        Motion(host, start, Vector2.Zero, false);
        Dispatch(host, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = start, GlobalPosition = start });
        await Frames(host, 2);
        Vector2 previous = start;
        bool keepHeld = false;
        try
        {
            for (int step = 1; step <= 3; step++)
            {
                Vector2 position = start + new Vector2(step * 12, step * 3);
                Motion(host, position, position - previous, true);
                previous = position;
                await Frames(host, 2);
            }
            if (!host.GetViewport().GuiIsDragging())
                throw new InvalidOperationException($"Actual GUI drag did not start from {source.Name}");
            Vector2 end = target.GetGlobalRect().GetCenter();
            Motion(host, end, end - previous, true);
            await Frames(host, 2);
            Viewport viewport = host.GetViewport();
            Variant payload = viewport.GuiGetDragData();
            Control? hovered = viewport.GuiGetHoveredControl();
            if (System.Environment.GetEnvironmentVariable("WEAPON_WORKBENCH_INPUT_DIAGNOSTICS") == "1")
            {
            GD.Print($"WORKBENCH_DRAG source={source.GetPath()} target={target.GetPath()} position={end} hovered={hovered?.GetPath()} payload={payload} context={diagnosticContext?.Invoke()}");
            if (payload.AsGodotObject() is FpsInventoryDragData data)
                GD.Print($"WORKBENCH_PAYLOAD owner={data.Owner.GetPath()} session={data.Session} arsenalIndex={data.ArsenalIndex} sourceSlot={data.SourceSlot} weapon={data.Weapon?.InstanceId} part={data.Part?.Key}");
            for (Control? candidate = hovered; candidate != null; candidate = candidate.GetParent() as Control)
            {
                Vector2 local = candidate.GetGlobalTransformWithCanvas().AffineInverse() * end;
                GD.Print($"WORKBENCH_DROP_ROUTE node={candidate.GetPath()} filter={candidate.MouseFilter} canDrop={candidate._CanDropData(local, payload)}");
            }
            }
            if (leaveHeld) { keepHeld = true; return; }
            Dispatch(host, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = end, GlobalPosition = end });
            await Frames(host, 2);
            if (host.GetViewport().GuiIsDragging()) throw new InvalidOperationException("Workbench GUI drag remains active after release");
        }
        finally
        {
            if (!keepHeld)
            {
                Dispatch(host, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            }
        }
    }

    public static void Release(Vector2 position, Node? host = null)
    {
        Dispatch(host, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position, GlobalPosition = position });
    }

    private static void Motion(Node host, Vector2 position, Vector2 relative, bool held)
    {
        Input.WarpMouse(position);
        Dispatch(host, new InputEventMouseMotion { Position = position, GlobalPosition = position, Relative = relative,
            ButtonMask = held ? MouseButtonMask.Left : (MouseButtonMask)0 });
    }

    private static void Dispatch(Node? host, InputEvent input)
    {
        // Dummy windows have no OS cursor to warp; local viewport coordinates keep
        // the same GUI drag dispatch independent of DisplayServer mouse state.
        if (host != null && DisplayServer.GetName() == "headless")
        {
            Viewport viewport = host.GetViewport();
            viewport.NotifyMouseEntered();
            viewport.PushInput(input, true);
        }
        else Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }

    public static async Task Frames(Node host, int count)
    {
        for (int frame = 0; frame < count; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
