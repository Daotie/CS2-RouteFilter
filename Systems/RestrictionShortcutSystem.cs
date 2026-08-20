using Game;

namespace RouteFilter.Systems;

public sealed partial class RestrictionShortcutSystem : GameSystemBase
{
    private RestrictionToolSystem m_RestrictionToolSystem = null!;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_RestrictionToolSystem = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
    }

    protected override void OnUpdate()
    {
        // The action stays null when key binding registration failed on every attempt;
        // the panel button remains the fallback in that case.
        if (Mod.ToggleTool == null || !Mod.ToggleTool.WasPressedThisFrame())
            return;

        m_RestrictionToolSystem.Toggle();
    }
}
