using Game.Common;
using Game.Tools;
using Colossal.Entities;
using Game.Prefabs;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>Shared target-specific prefab matching for controller/layout vehicle forms.</summary>
internal static class VehiclePrefabMatcher
{
    // Receipt revalidation must accept the same prefab sources as candidate matching.
    // A physical-only match remains evidence only while its controller chain still belongs
    // to this canonical vehicle. No unrelated/detached part may authorize a no-route result.
    internal static bool StillMatches(Entity physical, Entity canonical, Entity prefab, EntityManager manager)
    {
        if (prefab == Entity.Null || !Live(canonical, manager)) return false;
        if (HasPrefab(canonical, prefab, manager) || LayoutHasPrefab(canonical, prefab, manager)) return true;
        if (!PhysicalBelongsTo(physical, canonical, manager)) return false;
        return HasPrefab(physical, prefab, manager) || LayoutHasPrefab(physical, prefab, manager);
    }

    internal static bool PhysicalBelongsTo(Entity physical, Entity canonical, EntityManager manager)
    {
        var current = physical;
        for (var depth = 0; depth <= 4; depth++)
        {
            if (!Live(current, manager)) return false;
            if (current == canonical) return true;
            if (depth == 4 || !manager.TryGetComponent(current, out Controller controller) ||
                controller.m_Controller == Entity.Null || controller.m_Controller == current) return false;
            current = controller.m_Controller;
        }
        return false;
    }

    private static bool Live(Entity entity, EntityManager manager) => entity != Entity.Null && manager.Exists(entity) &&
        !manager.HasComponent<Deleted>(entity) && !manager.HasComponent<Temp>(entity);

    private static bool HasPrefab(Entity entity, Entity prefab, EntityManager manager) =>
        Live(entity, manager) && manager.TryGetComponent(entity, out PrefabRef reference) && reference.m_Prefab == prefab;

    private static bool LayoutHasPrefab(Entity entity, Entity prefab, EntityManager manager)
    {
        if (!manager.TryGetBuffer(entity, true, out DynamicBuffer<LayoutElement> layout)) return false;
        foreach (var part in layout) if (HasPrefab(part.m_Vehicle, prefab, manager)) return true;
        return false;
    }

    internal static bool TryMatch(
        Entity physicalVehicle,
        Entity canonicalVehicle,
        Entity target,
        ComponentLookup<PrefabRef> prefabRefs,
        BufferLookup<LayoutElement> layouts,
        NativeParallelMultiHashMap<Entity, Entity> targetPrefabs,
        out Entity matchedPrefab)
    {
        matchedPrefab = Entity.Null;

        if (TryMatchEntity(physicalVehicle, target, prefabRefs, targetPrefabs, out matchedPrefab))
            return true;

        if (canonicalVehicle != physicalVehicle &&
            TryMatchEntity(canonicalVehicle, target, prefabRefs, targetPrefabs, out matchedPrefab))
            return true;

        if (TryMatchLayout(canonicalVehicle, target, prefabRefs, layouts, targetPrefabs, out matchedPrefab))
            return true;

        return canonicalVehicle != physicalVehicle &&
               TryMatchLayout(physicalVehicle, target, prefabRefs, layouts, targetPrefabs, out matchedPrefab);
    }

    private static bool TryMatchEntity(
        Entity entity,
        Entity target,
        ComponentLookup<PrefabRef> prefabRefs,
        NativeParallelMultiHashMap<Entity, Entity> targetPrefabs,
        out Entity matchedPrefab)
    {
        matchedPrefab = Entity.Null;
        if (entity == Entity.Null || !prefabRefs.TryGetComponent(entity, out var prefabRef)) return false;
        if (!TargetRestricts(target, prefabRef.m_Prefab, targetPrefabs)) return false;
        matchedPrefab = prefabRef.m_Prefab;
        return true;
    }

    private static bool TryMatchLayout(
        Entity entity,
        Entity target,
        ComponentLookup<PrefabRef> prefabRefs,
        BufferLookup<LayoutElement> layouts,
        NativeParallelMultiHashMap<Entity, Entity> targetPrefabs,
        out Entity matchedPrefab)
    {
        matchedPrefab = Entity.Null;
        if (entity == Entity.Null || !layouts.TryGetBuffer(entity, out var layout)) return false;

        for (var i = 0; i < layout.Length; i++)
        {
            var part = layout[i].m_Vehicle;
            if (TryMatchEntity(part, target, prefabRefs, targetPrefabs, out matchedPrefab)) return true;
        }
        return false;
    }

    private static bool TargetRestricts(
        Entity target,
        Entity prefab,
        NativeParallelMultiHashMap<Entity, Entity> targetPrefabs)
    {
        if (target == Entity.Null || prefab == Entity.Null ||
            !targetPrefabs.TryGetFirstValue(target, out var restricted, out var iterator))
            return false;

        do
        {
            if (restricted == prefab) return true;
        } while (targetPrefabs.TryGetNextValue(out restricted, ref iterator));

        return false;
    }
}
