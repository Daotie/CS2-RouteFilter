using Game.Common;
using Game.Prefabs;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>Shared target-specific prefab matching for controller/layout vehicle forms.</summary>
internal static class VehiclePrefabMatcher
{
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
