using Game;
using Game.Tools;
using Unity.Entities;

namespace NodeGate.Systems;

public sealed partial class RestrictionShortcutSystem : GameSystemBase
{
    private ToolSystem m_ToolSystem = null!;
    private DefaultToolSystem m_DefaultToolSystem = null!;
    private RestrictionToolSystem m_RestrictionToolSystem = null!;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        m_DefaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
        m_RestrictionToolSystem = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
    }

    protected override void OnUpdate()
    {
        if (!Mod.ToggleTool.WasPressedThisFrame())
            return;

        m_ToolSystem.selected = Entity.Null;
        m_ToolSystem.activeTool = m_ToolSystem.activeTool == m_RestrictionToolSystem
            ? m_DefaultToolSystem
            : m_RestrictionToolSystem;
    }
}
