using System.Collections.Generic;
using Colossal.Entities;
using Game.Common;
using RouteFilter.Persistence;
using Unity.Entities;
using Unity.Mathematics;

namespace RouteFilter.Systems;

public sealed partial class RestrictionToolSystem
{
    internal struct ApproachBar
    {
        internal RestrictionEntryIdentity Identity;
        internal float3 Start, End;
        internal bool Enabled;
    }
    private readonly List<ApproachBar> m_EntryBars = new();
    private HashSet<RestrictionEntryIdentity> m_PendingEntries;
    private int m_EntryRevision = -1;
    private bool m_SelectedWasUpdated;
    internal IReadOnlyList<ApproachBar> EntryBars => m_EntryBars;
    public bool EntryDirectionsSupported { get; private set; }
    public int EntryDirectionCount => m_EntryBars.Count;
    public int EnabledEntryDirectionCount { get; private set; }

    private void ClearEntryEditor()
    {
        m_EntryBars.Clear(); m_PendingEntries = null; m_EntryRevision = -1;
        EntryDirectionsSupported = false; EnabledEntryDirectionCount = 0; m_SelectedWasUpdated = false;
    }

    private void LoadEntryEditor()
    {
        ClearEntryEditor();
        var entries = World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().GetDirectionIntent(SelectedTarget);
        if (entries != null) m_PendingEntries = new HashSet<RestrictionEntryIdentity>(entries);
        RebuildEntryEditor(true);
    }

    // Constant cost while selected: one revision and one target tag. Geometry/topology
    // is derived only on selection or a dirty transition, never on cursor movement.
    private void RefreshEntryEditor()
    {
        if (SelectedTarget == Entity.Null || (SelectedTransportMode & 1) == 0) return;
        var updated = EntityManager.HasComponent<Updated>(SelectedTarget);
        var dirty = updated && !m_SelectedWasUpdated;
        m_SelectedWasUpdated = updated;
        if (dirty || m_EntryRevision != World.GetOrCreateSystemManaged<RestrictionIndexSystem>().Revision)
            RebuildEntryEditor(dirty);
    }

    private void RebuildEntryEditor(bool force)
    {
        m_EntryBars.Clear(); EnabledEntryDirectionCount = 0; EntryDirectionsSupported = false;
        if (SelectedTarget == Entity.Null || (SelectedTransportMode & 1) == 0) return;
        var index = World.GetOrCreateSystemManaged<RestrictionIndexSystem>();
        var groups = index.GetEditorEntries(SelectedTarget, force);
        m_EntryRevision = index.Revision;
        if (groups.Count > RouteFilterSaveData.MaxEntryCount) return;
        var valid = groups.Count > 0;
        var identities = new HashSet<RestrictionEntryIdentity>();
        foreach (var group in groups) { valid &= group.CustomSupported; identities.Add(group.Identity); }
        if (m_PendingEntries != null) foreach (var entry in m_PendingEntries) valid &= identities.Contains(entry);
        foreach (var group in groups)
        {
            if (!index.TryGetApproachGeometry(group, out var start, out var end)) { valid = false; continue; }
            m_EntryBars.Add(new ApproachBar { Identity = group.Identity, Start = start, End = end,
                Enabled = m_PendingEntries == null || m_PendingEntries.Contains(group.Identity) });
        }
        EntryDirectionsSupported = valid;
        UpdateEntryBarStates();
    }

    public void SetAllEntryDirections()
    {
        m_PendingEntries = null; UpdateEntryBarStates();
    }

    private void UpdateEntryBarStates()
    {
        EnabledEntryDirectionCount = 0;
        for (var i = 0; i < m_EntryBars.Count; i++)
        {
            var bar = m_EntryBars[i];
            bar.Enabled = !EntryDirectionsSupported || m_PendingEntries == null || m_PendingEntries.Contains(bar.Identity);
            if (bar.Enabled) EnabledEntryDirectionCount++;
            m_EntryBars[i] = bar;
        }
    }

    private RestrictionEntryIdentity[] PendingEntries()
    {
        if (!EntryDirectionsSupported || m_PendingEntries == null) return null;
        var entries = new RestrictionEntryIdentity[m_PendingEntries.Count]; m_PendingEntries.CopyTo(entries); return entries;
    }

    private bool TryToggleEntry(float3 hit)
    {
        if (!EntryDirectionsSupported || !World.GetOrCreateSystemManaged<RestrictionPersistenceSystem>().ConfigurationEditable) return false;
        var selected = -1; var nearest = 9f;
        for (var i = 0; i < m_EntryBars.Count; i++)
        {
            var bar = m_EntryBars[i]; var delta = bar.End.xz - bar.Start.xz;
            var t = math.saturate(math.dot(hit.xz - bar.Start.xz, delta) / math.max(.01f, math.lengthsq(delta)));
            var point = math.lerp(bar.Start, bar.End, t);
            var distance = math.distancesq(hit.xz, point.xz);
            if (math.abs(hit.y - point.y) < 4f && distance < nearest) { nearest = distance; selected = i; }
        }
        if (selected < 0) return false;
        if (m_PendingEntries == null)
        {
            m_PendingEntries = new HashSet<RestrictionEntryIdentity>();
            foreach (var bar in m_EntryBars) m_PendingEntries.Add(bar.Identity);
        }
        var identity = m_EntryBars[selected].Identity;
        if (!m_PendingEntries.Remove(identity)) m_PendingEntries.Add(identity);
        UpdateEntryBarStates(); return true;
    }
}
