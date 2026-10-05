using System;
using Colossal.Entities;
using Colossal.UI.Binding;
using Game.Common;
using Game.Net;
using Game.UI.InGame;
using RouteFilter.Components;
using Unity.Entities;

namespace RouteFilter.Systems;

// Uses the native selection lifecycle and native information-section registry.
public sealed class RouteFilterSignInfoSection : InfoSectionBase
{
    protected override string group => Mod.Id;
    private Entity m_Target, m_Connection;
    private int m_Assets, m_Entries, m_Revision = -1;
    private int m_EntryOrdinal;
    private string m_Categories = "", m_TargetName = "";
    protected override void OnCreate()
    {
        base.OnCreate();
        m_InfoUISystem.AddMiddleSection(this);
        AddBinding(new TriggerBinding(Mod.Id,"editSelectedSign", () =>
        {
            if (!TryResolve(EntityManager,m_InfoUISystem.selectedEntity,out var owner)) return;
            var tool = World.GetOrCreateSystemManaged<RestrictionToolSystem>();
            Mod.SelectedTargetMode = EntityManager.HasComponent<Node>(owner.Target) ? RestrictionTargetMode.Node : RestrictionTargetMode.Segment;
            tool.Activate(); tool.SelectTarget(owner.Target);
        }));
    }
    internal static bool TryResolve(EntityManager manager,Entity entity,out RoadRestrictionSignOwner owner)
    {
        owner = default;
        for (var depth=0; depth<8 && entity != Entity.Null && manager.Exists(entity); depth++)
        {
            if (manager.HasComponent<Deleted>(entity)) return false;
            if (manager.TryGetComponent(entity,out owner))
                return owner.Target != Entity.Null && manager.Exists(owner.Target) && !manager.HasComponent<Deleted>(owner.Target);
            if (!manager.TryGetComponent(entity,out Owner parent) || parent.m_Owner == entity) break;
            entity = parent.m_Owner;
        }
        return false;
    }
    protected override void OnPreUpdate()
    {
        var revision = World.GetExistingSystemManaged<RestrictionGroundIndicatorSystem>()?.Revision ?? 0;
        if (revision != m_Revision) { m_Revision = revision; RequestUpdate(); m_InfoUISystem.SetDirty(); }
    }
    protected override void Reset() { visible = false; m_Target = m_Connection = Entity.Null; m_Assets = m_Entries = 0; }
    protected override void OnUpdate()
    {
        if (!TryResolve(EntityManager,selectedEntity,out var owner)) return;
        visible = true; m_Target = owner.Target; m_Connection = owner.Connection; m_EntryOrdinal = owner.EntryOrdinal;
    }
    protected override void OnProcess()
    {
        m_Assets=0; m_Entries=0;
        if (EntityManager.TryGetBuffer(m_Target,true,out DynamicBuffer<RestrictedVehicleAssetV1> assets)) m_Assets = assets.Length;
        m_TargetName = m_NameSystem.GetRenderedLabelName(m_Target) ?? "";
        m_Categories = string.Join(" · ",World.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.Labels(m_Target) ?? Array.Empty<string>());
        foreach (var entry in World.GetOrCreateSystemManaged<RestrictionIndexSystem>().GetAppliedRoadEntries(m_Target)) if (entry.Enabled) m_Entries++;
    }
    public override void OnWriteProperties(IJsonWriter writer)
    {
        writer.PropertyName("targetKind"); writer.Write(EntityManager.HasComponent<Node>(m_Target) ? "Node" : "Segment");
        writer.PropertyName("assets"); writer.Write(m_Assets);
        writer.PropertyName("entries"); writer.Write(m_Entries);
        writer.PropertyName("connection"); writer.Write(m_Connection != Entity.Null);
        writer.PropertyName("entryOrdinal"); writer.Write(m_EntryOrdinal);
        writer.PropertyName("owner"); writer.Write("RouteFilter");
        writer.PropertyName("targetName"); writer.Write(m_TargetName);
        writer.PropertyName("categories"); writer.Write(m_Categories);
    }
}
