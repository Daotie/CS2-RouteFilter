// Managed adapters exercise the production scan claim policy, not Unity scheduling.
namespace Unity.Entities
{
    public record struct Entity(int Index);
}
namespace Unity.Collections
{
    public struct NativeParallelHashSet<T> where T : notnull
    {
        private readonly HashSet<T> values;
        public NativeParallelHashSet() { values = new(); }
        public int Count() => values.Count;
        public bool Add(T value) => values.Add(value);
    }
}
