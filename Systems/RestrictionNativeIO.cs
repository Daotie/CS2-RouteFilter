using System;
using Colossal.Serialization.Entities;
using RouteFilter.Persistence;
using Unity.Collections;

namespace RouteFilter.Systems;

/// <summary>Native framing shared by persistence and isolated in-game protocol fixtures.</summary>
internal static class RestrictionNativeIO
{
    internal static void WriteBody<TWriter>(TWriter writer, byte[] bytes) where TWriter : IWriter
    {
        if (bytes == null || bytes.Length < 8 || bytes.Length > RestrictionByteSink.MaxPayloadBytes)
            throw new InvalidOperationException("Invalid RouteFilter payload length");
        writer.Write(bytes.Length);
        using var native = new NativeArray<byte>(bytes, Allocator.Temp);
        writer.Write(native);
    }

    // The caller already consumed the discriminator. Never read its length again.
    internal static byte[] ReadBody<TReader>(TReader reader, int length) where TReader : IReader
    {
        if (length < 8 || length > RestrictionByteSink.MaxPayloadBytes)
            throw new InvalidOperationException("Invalid RouteFilter payload length");
        using var native = new NativeArray<byte>(length, Allocator.Temp);
        reader.Read(native);
        return native.ToArray();
    }

    internal static string ReadName<TReader>(TReader reader) where TReader : IReader
    {
        reader.Read(out int length);
        if (length < 0 || length > RouteFilterSaveData.MaxPrefabNameLength)
            throw new InvalidOperationException("Invalid legacy prefab name length");
        var chars = new char[length];
        for (var i = 0; i < length; i++) reader.Read(out chars[i]);
        return new string(chars);
    }
}
