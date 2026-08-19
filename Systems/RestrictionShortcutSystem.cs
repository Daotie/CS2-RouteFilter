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
        if (!Mod.ToggleTool.WasPressedThisFrame())
            return;

        m_RestrictionToolSystem.Toggle();
    }
}
