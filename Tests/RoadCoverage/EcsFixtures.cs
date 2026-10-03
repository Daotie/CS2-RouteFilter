// In-memory adapters for testing the production matcher, not a Unity runtime simulation.
namespace Colossal.Entities { }
namespace Game.Common { public struct Deleted { } }
namespace Game.Tools { public struct Temp { } }
namespace Game.Prefabs { public struct PrefabRef { public Unity.Entities.Entity m_Prefab; } }
namespace Game.Vehicles
{
    public struct Controller { public Unity.Entities.Entity m_Controller; }
    public struct LayoutElement { public Unity.Entities.Entity m_Vehicle; }
}
namespace Unity.Entities
{
    public record struct Entity(int Index) { public static readonly Entity Null = default; }
    public class EntityManager
    {
        private readonly HashSet<Entity> entities = new();
        private readonly Dictionary<(Entity, Type), object> data = new();
        public Entity Create(int id) { var e = new Entity(id); entities.Add(e); return e; }
        public bool Exists(Entity e) => entities.Contains(e);
        public bool HasComponent<T>(Entity e) => data.ContainsKey((e, typeof(T)));
        public void Set<T>(Entity e, T value) => data[(e, typeof(T))] = value!;
        public bool TryGetComponent<T>(Entity e, out T value)
        {
            if (data.TryGetValue((e, typeof(T)), out var item)) { value = (T)item; return true; }
            value = default!; return false;
        }
        public bool TryGetBuffer<T>(Entity e, bool readOnly, out DynamicBuffer<T> value) => TryGetComponent(e, out value);
    }
    public class DynamicBuffer<T> : List<T> { public int Length => Count; }
    public readonly struct ComponentLookup<T>(EntityManager manager)
    { public bool TryGetComponent(Entity e, out T value) => manager.TryGetComponent(e, out value); }
    public readonly struct BufferLookup<T>(EntityManager manager)
    { public bool TryGetBuffer(Entity e, out DynamicBuffer<T> value) => manager.TryGetBuffer(e, true, out value); }
}
namespace Unity.Collections
{
    public struct NativeParallelMultiHashMapIterator<K> { internal K Key; internal int Index; }
    public class NativeParallelMultiHashMap<K, V> where K : notnull
    {
        private readonly Dictionary<K, List<V>> values = new();
        public void Add(K key, V value) { if (!values.TryGetValue(key, out var list)) values[key] = list = new(); list.Add(value); }
        public bool TryGetFirstValue(K key, out V value, out NativeParallelMultiHashMapIterator<K> iterator)
        { iterator = new() { Key = key, Index = -1 }; return TryGetNextValue(out value, ref iterator); }
        public bool TryGetNextValue(out V value, ref NativeParallelMultiHashMapIterator<K> iterator)
        {
            if (values.TryGetValue(iterator.Key, out var list) && ++iterator.Index < list.Count)
            { value = list[iterator.Index]; return true; }
            value = default!; return false;
        }
    }
}
