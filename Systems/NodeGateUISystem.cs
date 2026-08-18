using Colossal.UI.Binding;
using Game;
using Game.Tools;
using Game.UI;
using NodeGate.Components;
using Unity.Entities;

namespace NodeGate.Systems;

public sealed partial class NodeGateUISystem : UISystemBase
{
    private ToolSystem m_ToolSystem = null!;
    private RestrictionToolSystem m_RestrictionTool = null!;

    public override GameMode gameMode => GameMode.GameOrEditor;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        m_RestrictionTool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
        AddUpdateBinding(new GetterValueBinding<bool>(Mod.Id, "toolActive", () => m_ToolSystem.activeTool == m_RestrictionTool));
        AddUpdateBinding(new GetterValueBinding<int>(Mod.Id, "selectedMask", () => unchecked((int)Mod.SelectedVehicleTypes)));
        AddBinding(new TriggerBinding(Mod.Id, "toggleTool", m_RestrictionTool.Toggle));
        AddBinding(new TriggerBinding<int>(Mod.Id, "toggleVehicle", ToggleVehicle));
        AddBinding(new TriggerBinding(Mod.Id, "selectAll", () => Mod.SelectedVehicleTypes = VehicleTypeMask.All));
        AddBinding(new TriggerBinding(Mod.Id, "selectNone", () => Mod.SelectedVehicleTypes = VehicleTypeMask.None));
    }

    private static void ToggleVehicle(int rawValue)
    {
        var value = (VehicleTypeMask)(uint)rawValue & VehicleTypeMask.All;
        Mod.SelectedVehicleTypes ^= value;
    }
}
