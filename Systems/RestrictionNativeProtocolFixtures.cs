using System;
using Colossal.Serialization.Entities;
using RouteFilter.Persistence;
using Unity.Collections;
using Unity.Entities;

namespace RouteFilter.Systems;

/// <summary>
/// Explicit, bounded diagnostic using game-native buffers only. No world entities, city data,
/// persistence system instance, disk writes or jobs are used. Work is O(fixed fixture bytes).
/// This verifies primitive protocol alignment, not a complete city save/load integration.
/// </summary>
internal static class RestrictionNativeProtocolFixtures
{
    internal static void Run()
    {
        // A writer map indexes original entities; a reader map indexes serialized IDs.
        var original = new Entity { Index = 3, Version = 7 };
        var remapped = new Entity { Index = 101, Version = 9 };
        using var writeMap = new NativeArray<Entity>(new[] { Entity.Null, Entity.Null, Entity.Null,
            new Entity { Index = 1, Version = original.Version } }, Allocator.Temp);
        using var readMap = new NativeArray<Entity>(new[] { Entity.Null, remapped }, Allocator.Temp);
        using var buffer = new NativeList<byte>(1024, Allocator.Temp);
        var writer = new BinaryWriter();
        writer.Initialize(default, buffer, writeMap);
        const int sentinel = 0x13572468;
        const string name = "车辆Car01";
        var legacyBlock = writer.Begin();
        writer.Write(2); // Historical version, one node target, zero segment targets.
        writer.Write(1);
        writer.Write(original);
        writer.Write(1);
        writer.Write(name);
        writer.Write(0);
        Require(writer.End(legacyBlock), "legacy writer block");
        writer.Write(sentinel);

        var data = new RouteFilterSaveData();
        data.PrefabNames.Add(name);
        data.Restrictions.Add(new PersistentRestriction
        {
            Target = new RestrictionTargetIdentity { Kind = 0, Anchor = new RestrictionAnchor(1, 2, 3) },
            PrefabIndices = new[] { 0 }
        });
        var sink = new RestrictionByteSink();
        RouteFilterSaveCodec.Encode(data, sink);
        var bytes = sink.ToArray();
        var bodyBlock = writer.Begin();
        RestrictionNativeIO.WriteBody(writer, bytes);
        Require(writer.End(bodyBlock), "body writer block");
        writer.Write(sentinel);

        using var position = new NativeReference<int>(Allocator.Temp);
        var reader = new BinaryReader();
        reader.Initialize(default, buffer.AsArray(), position, readMap);
        var block = reader.Begin(out int legacySize);
        reader.Read(out int version);
        reader.Read(out int count);
        reader.Read(out Entity target);
        reader.Read(out int assetCount);
        var decodedName = RestrictionNativeIO.ReadName(reader);
        reader.Read(out int segments);
        Require(version == 2 && count == 1 && target == remapped && assetCount == 1 &&
            decodedName == name && segments == 0, "legacy entity remap / UTF16");
        Require(legacySize == 24 + name.Length * 2, "single-index legacy layout");
        Require(reader.End(block), "legacy exact outer alignment");
        reader.Read(out int firstSentinel);
        Require(firstSentinel == sentinel, "legacy following data");
        block = reader.Begin(out int bodySize);
        reader.Read(out int length);
        var restored = RestrictionNativeIO.ReadBody(reader, length);
        Require(bodySize == 4 + bytes.Length && Same(bytes, restored), "native raw body framing");
        var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(restored));
        Require(decoded.IsApplicable && decoded.Data.Restrictions.Count == 1 &&
            decoded.Data.PrefabNames[0] == name, "schema codec native roundtrip");
        Require(reader.End(block), "body exact outer alignment");
        reader.Read(out int lastSentinel);
        Require(lastSentinel == sentinel && position.Value == buffer.Length, "final alignment");
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void Require(bool valid, string check)
    {
        if (!valid) throw new InvalidOperationException("Native protocol fixture failed: " + check);
    }
}
