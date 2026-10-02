using System;
using System.Collections.Generic;

namespace RouteFilter.Persistence;

/// <summary>
/// Byte-oriented sink used by <see cref="RouteFilterSaveCodec"/>. Runtime and offline fixtures
/// use the same bounded managed byte encoder; native framing is handled separately.
/// </summary>
public interface IRestrictionSaveSink
{
    void WriteUInt(uint value);
    void WriteUShort(ushort value);
    void WriteInt(int value);
    void WriteFloat(float value);
    void WriteString(string value);
    void WriteBytes(byte[] value);
}

/// <summary>Byte-oriented source with explicit failure, so partial parses can never be mistaken for success.</summary>
public interface IRestrictionSaveSource
{
    /// <summary>Bytes still unread in the bounded managed body.</summary>
    long Remaining { get; }
    bool ReadUInt(out uint value);
    bool ReadUShort(out ushort value);
    bool ReadInt(out int value);
    bool ReadString(out string value);
    /// <summary>
    /// Consumes the bounded managed remainder. This does not validate or resynchronise the
    /// game's surrounding native stream.
    /// </summary>
    bool SkipToEnd();
    /// <summary>Copies the remainder verbatim; used to protect a payload written by a newer schema.</summary>
    bool ReadRemainingBytes(out byte[] value);
}

/// <summary>
/// Fixed-point world position. Deliberately plain three integers: the persistence layer must not
/// depend on the Unity mathematics library, so the format contract can be verified offline.
/// </summary>
public struct RestrictionAnchor : IEquatable<RestrictionAnchor>
{
    public int X;
    public int Y;
    public int Z;

    public RestrictionAnchor(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public bool Equals(RestrictionAnchor other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object obj) => obj is RestrictionAnchor other && Equals(other);

    public override int GetHashCode()
    {
        unchecked { return ((X * 397) ^ Y) * 397 ^ Z; }
    }

    public override string ToString() => $"({X},{Y},{Z})";

    /// <summary>~3.9 mm. Exact for geometry that round-trips bit-identically.</summary>
    public const float PositionScale = 256f;

    public static RestrictionAnchor Quantize(float x, float y, float z)
        => new RestrictionAnchor(
            (int)Math.Round(x * PositionScale, MidpointRounding.AwayFromZero),
            (int)Math.Round(y * PositionScale, MidpointRounding.AwayFromZero),
            (int)Math.Round(z * PositionScale, MidpointRounding.AwayFromZero));
}

/// <summary>
/// Where a restriction target lives, in terms that survive a save/load cycle.
///
/// A raw Entity is not a persistent identity and is not written by this format. Geometry is
/// quantized and matched exactly, with ambiguous identities refused. It is not guaranteed to
/// identify a target across arbitrary network edits; see the documented compatibility limit.
/// </summary>
public struct RestrictionTargetIdentity : IEquatable<RestrictionTargetIdentity>
{
    /// <summary>0 = Node, 1 = Segment. Matches <c>RestrictionTargetMode</c>.</summary>
    public byte Kind;
    public RestrictionAnchor Anchor;
    /// <summary>Segment endpoint, unused for nodes.</summary>
    public RestrictionAnchor EndAnchor;
    /// <summary>Rounded segment length in centimetres; 0 for nodes.</summary>
    public int LengthCentimetres;

    /// <summary>Exact identity test. Never a "nearest neighbour" search.</summary>
    public bool Matches(in RestrictionTargetIdentity other)
        => Kind == other.Kind && Anchor.Equals(other.Anchor) &&
           (Kind == 0 || (EndAnchor.Equals(other.EndAnchor) &&
                          LengthCentimetres == other.LengthCentimetres));

    public bool Equals(RestrictionTargetIdentity other) => Matches(other);
    public override bool Equals(object value) => value is RestrictionTargetIdentity other && Matches(other);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Anchor.GetHashCode() * 397 ^ Kind;
            return Kind == 0 ? hash : (hash * 397 ^ EndAnchor.GetHashCode()) * 397 ^ LengthCentimetres;
        }
    }
}
/// <summary>
/// One persistent restriction: "these prefabs are forbidden at this target".
/// This is the complete player intent and nothing else. No topology, no candidate, no safety
/// verdict, no lease, no attempt, no graph publication state.
/// </summary>
public struct PersistentRestriction
{
    public RestrictionTargetIdentity Target;
    /// <summary>Indices into <see cref="RouteFilterSaveData.PrefabNames"/>. Unresolved names stay in the table.</summary>
    public int[] PrefabIndices;
}

/// <summary>Decoded payload. Field order is the on-disk order.</summary>
public sealed class RouteFilterSaveData
{
    /// <summary>'R','F','L','T' little-endian. Any other value means "not a RouteFilter payload".</summary>
    public const uint Magic = 0x544C4652u;

    /// <summary>
    /// Bumped whenever the on-disk body changes. A payload whose schema is newer than this is
    /// preserved byte for byte and never reinterpreted.
    /// </summary>
    public const ushort SchemaVersion = 4;

    /// <summary>Older schemas this build can still read and migrate.</summary>
    public const ushort OldestSupportedSchema = 3;

    /// <summary>
    /// Hard sanity bounds. These are not "expected values"; they exist so that a corrupt count
    /// cannot make the codec try to allocate or read gigabytes before failing.
    /// </summary>
    public const int MaxRestrictionCount = 200_000;
    public const int MaxPrefabNameCount = 20_000;
    public const int MaxPrefabNameLength = 512;
    public const int MaxPrefabsPerRestriction = 4_096;

    public ushort Schema;
    public List<string> PrefabNames = new();
    public List<PersistentRestriction> Restrictions = new();

    public static RouteFilterSaveData CreateEmpty() => new();
}

public enum SaveDecodeStatus
{
    Ok = 0,
    /// <summary>Not a RouteFilter payload and not a recognised legacy layout.</summary>
    NotRouteFilterData = 1,
    /// <summary>RouteFilter payload written by a newer build. Must be preserved, never rewritten.</summary>
    FutureSchema = 2,
    /// <summary>Recognised header, unusable body. Enforcement must be disabled.</summary>
    Corrupt = 3,
    /// <summary>Header present; some individual records were dropped, the rest were usable.</summary>
    PartiallyRecovered = 4
}

public sealed class SaveDecodeResult
{
    public SaveDecodeStatus Status = SaveDecodeStatus.Ok;
    public RouteFilterSaveData Data;
    /// <summary>Exact bytes of a payload this build must not interpret. Re-emitted verbatim on save.</summary>
    public byte[] ForeignPayload;
    public ushort ForeignSchema;
    public int DroppedRecords;
    public int DroppedPrefabNames;
    public string Detail = string.Empty;

    /// <summary>
    /// True only when the decoded data is safe to hand to the restore pipeline. A corrupt payload
    /// reports false even if some records parsed, so a caller cannot accidentally half-apply it.
    /// </summary>
    public bool IsApplicable =>
        Status == SaveDecodeStatus.Ok || Status == SaveDecodeStatus.PartiallyRecovered;
}

/// <summary>
/// The whole on-disk format, in one place.
///
/// Layout:
/// <code>
/// uint    magic                'RFLT'
/// ushort  schema               4 (schema 3 is accepted without a checksum)
/// ushort  flags                bit 0: payload is RouteFilter-owned (reserved, currently 0)
/// int     prefabNameCount
/// string  prefabName           x prefabNameCount      (UTF-16, deduplicated)
/// int     restrictionCount
///   int     kind               0 node, 1 segment
///   int3    anchor             quantized world position of the node / first segment endpoint
///   int3    endAnchor          quantized second endpoint (segments only)
///   int     lengthCentimetres  segment length (segments only)
///   int     prefabCount
///   int     prefabIndex        x prefabCount         (indices into the name table)
/// uint    checksum             schema 4 only, CRC32 of all preceding body bytes
/// </code>
///
/// Two deliberate choices:
/// <list type="number">
/// <item>Prefab names live in one table instead of being repeated per restriction, which keeps a
/// hundred-target city readable without introducing any compression or binary trickery.</item>
/// <item>Every count is validated before it is used, and every record is validated independently,
/// so one bad record costs one record and never the file.</item>
/// </list>
/// </summary>
public static class RouteFilterSaveCodec
{
    public static void Encode(RouteFilterSaveData data, IRestrictionSaveSink sink)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (sink == null) throw new ArgumentNullException(nameof(sink));
        if (data.PrefabNames.Count > RouteFilterSaveData.MaxPrefabNameCount ||
            data.Restrictions.Count > RouteFilterSaveData.MaxRestrictionCount)
            throw new ArgumentException("Restriction intent exceeds save limits");
        foreach (var name in data.PrefabNames)
            if (name != null && name.Length > RouteFilterSaveData.MaxPrefabNameLength)
                throw new ArgumentException("Prefab identifier exceeds save limits");
        foreach (var record in data.Restrictions)
            if ((record.PrefabIndices?.Length ?? 0) > RouteFilterSaveData.MaxPrefabsPerRestriction)
                throw new ArgumentException("Target prefab count exceeds save limits");
        var body = new RestrictionByteSink();
        EncodeBody(data, body);
        var bytes = body.ToArray();
        sink.WriteBytes(bytes);
        sink.WriteUInt(Checksum(bytes));
    }

    // CRC detects accidental bit damage to names/target identities that would otherwise decode
    // into a plausible but incorrect restriction. It is not an authenticity/security mechanism.
    private static uint Checksum(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        }
        return ~crc;
    }

    private static void EncodeBody(RouteFilterSaveData data, IRestrictionSaveSink sink)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        sink.WriteUInt(RouteFilterSaveData.Magic);
        sink.WriteUShort(RouteFilterSaveData.SchemaVersion);
        sink.WriteUShort(0);

        sink.WriteInt(data.PrefabNames.Count);
        for (var i = 0; i < data.PrefabNames.Count; i++)
            sink.WriteString(data.PrefabNames[i] ?? string.Empty);

        sink.WriteInt(data.Restrictions.Count);
        for (var i = 0; i < data.Restrictions.Count; i++)
        {
            var restriction = data.Restrictions[i];
            var target = restriction.Target;
            sink.WriteInt(target.Kind);
            sink.WriteInt(target.Anchor.X);
            sink.WriteInt(target.Anchor.Y);
            sink.WriteInt(target.Anchor.Z);
            sink.WriteInt(target.EndAnchor.X);
            sink.WriteInt(target.EndAnchor.Y);
            sink.WriteInt(target.EndAnchor.Z);
            sink.WriteInt(target.LengthCentimetres);

            var indices = restriction.PrefabIndices;
            var count = indices?.Length ?? 0;
            sink.WriteInt(count);
            for (var j = 0; j < count; j++) sink.WriteInt(indices[j]);
        }
    }

    public static SaveDecodeResult Decode(IRestrictionSaveSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var result = new SaveDecodeResult();

        if (!source.ReadUInt(out var magic))
        {
            result.Status = SaveDecodeStatus.NotRouteFilterData;
            result.Detail = "payload shorter than the magic";
            return result;
        }

        if (magic != RouteFilterSaveData.Magic)
        {
            result.Status = SaveDecodeStatus.NotRouteFilterData;
            result.Detail = $"unexpected magic 0x{magic:X8}";
            return result;
        }

        if (!source.ReadUShort(out var schema) || !source.ReadUShort(out var flags))
        {
            result.Status = SaveDecodeStatus.Corrupt;
            result.Detail = "truncated header";
            return result;
        }
        _ = flags;

        result.ForeignSchema = schema;
        if (schema > RouteFilterSaveData.SchemaVersion || flags != 0)
        {
            // Protect the future: keep the bytes, refuse to interpret them, refuse to rewrite them.
            byte[] raw = Array.Empty<byte>();
            if (source.Remaining == 0 || source.ReadRemainingBytes(out raw))
            {
                result.Status = SaveDecodeStatus.FutureSchema;
                var preserved = new RestrictionByteSink();
                preserved.WriteUInt(magic);
                preserved.WriteUShort(schema);
                preserved.WriteUShort(flags);
                preserved.WriteBytes(raw);
                result.ForeignPayload = preserved.ToArray();
                result.Detail = flags != 0 ? $"unsupported header flags 0x{flags:X4}" :
                    $"payload schema {schema} is newer than supported {RouteFilterSaveData.SchemaVersion}";
                return result;
            }
            result.Status = SaveDecodeStatus.Corrupt;
            result.Detail = $"payload schema {schema} is newer than supported but its body could not be preserved";
            return result;
        }

        if (schema < RouteFilterSaveData.OldestSupportedSchema)
        {
            result.Status = SaveDecodeStatus.Corrupt;
            result.Detail = $"payload schema {schema} is older than the oldest supported {RouteFilterSaveData.OldestSupportedSchema}";
            return result;
        }

        if (schema == 4)
        {
            if (!source.ReadRemainingBytes(out var rest) || rest.Length < 4)
                return ChecksumFailure("truncated checksum");
            var bodyLength = rest.Length - 4;
            var envelope = new RestrictionByteSink();
            envelope.WriteUInt(magic); envelope.WriteUShort(schema); envelope.WriteUShort(flags);
            var body = new byte[bodyLength];
            Array.Copy(rest, body, bodyLength);
            envelope.WriteBytes(body);
            if (Checksum(envelope.ToArray()) != BitConverter.ToUInt32(rest, bodyLength))
                return ChecksumFailure("payload checksum mismatch");
            source = new RestrictionByteSource(body);
        }

        var data = RouteFilterSaveData.CreateEmpty();
        data.Schema = schema;
        result.Data = data;

        if (!source.ReadInt(out var nameCount))
        {
            result.Status = SaveDecodeStatus.Corrupt;
            result.Data = null;
            result.Detail = "truncated prefab name table";
            return result;
        }
        if (nameCount < 0 || nameCount > RouteFilterSaveData.MaxPrefabNameCount)
        {
            result.Status = SaveDecodeStatus.Corrupt;
            result.Data = null;
            result.Detail = $"implausible prefab name count {nameCount}";
            return result;
        }
        for (var i = 0; i < nameCount; i++)
        {
            if (!source.ReadString(out var name) || name.Length > RouteFilterSaveData.MaxPrefabNameLength)
            {
                result.Detail = $"truncated or oversized prefab name at {i}";
                result.Status = SaveDecodeStatus.Corrupt;
                result.Data = null;
                return result;
            }
            data.PrefabNames.Add(name); // Keep table slots, including empty names: indices must not shift.
        }

        if (!source.ReadInt(out var restrictionCount))
        {
            result.Status = SaveDecodeStatus.Corrupt;
            result.Data = null;
            result.Detail = "truncated restriction table";
            return result;
        }
        if (restrictionCount < 0 || restrictionCount > RouteFilterSaveData.MaxRestrictionCount)
        {
            result.Status = SaveDecodeStatus.Corrupt;
            result.Data = null;
            result.Detail = $"implausible restriction count {restrictionCount}";
            return result;
        }

        var corrupted = result.DroppedPrefabNames != 0;
        for (var i = 0; i < restrictionCount; i++)
        {
            if (!TryReadRestriction(source, data, out _))
            {
                result.DroppedRecords++;
                corrupted = true;
                if (!source.SkipToEnd()) break;
                continue;
            }
        }

        if (!corrupted && source.Remaining != 0)
        {
            result.Status = SaveDecodeStatus.Corrupt;
            result.Data = null;
            result.Detail = "unrecognized trailing bytes";
            return result;
        }
        result.Status = corrupted ? SaveDecodeStatus.PartiallyRecovered : SaveDecodeStatus.Ok;
        if (corrupted)
            result.Detail = $"recovered {data.Restrictions.Count} of {restrictionCount} restrictions, " +
                            $"{result.DroppedRecords} dropped, {result.DroppedPrefabNames} unusable prefab names";
        return result;
    }

    private static SaveDecodeResult ChecksumFailure(string detail)
        => new SaveDecodeResult { Status = SaveDecodeStatus.Corrupt, Detail = detail };

    private static bool TryReadRestriction(
        IRestrictionSaveSource source,
        RouteFilterSaveData data,
        out PersistentRestriction restriction)
    {
        restriction = default;
        if (!source.ReadInt(out var kindRaw)) return false;
        if (kindRaw < 0 || kindRaw > 1) return false;

        if (!source.ReadInt(out var ax) || !source.ReadInt(out var ay) || !source.ReadInt(out var az)) return false;
        if (!source.ReadInt(out var bx) || !source.ReadInt(out var by) || !source.ReadInt(out var bz)) return false;
        if (!source.ReadInt(out var length)) return false;

        var target = new RestrictionTargetIdentity
        {
            Kind = (byte)kindRaw,
            Anchor = new RestrictionAnchor(ax, ay, az),
            EndAnchor = new RestrictionAnchor(bx, by, bz),
            LengthCentimetres = length
        };

        if (!source.ReadInt(out var prefabCount)) return false;
        if (prefabCount < 0 || prefabCount > RouteFilterSaveData.MaxPrefabsPerRestriction) return false;

        var indices = prefabCount > 0 ? new int[prefabCount] : Array.Empty<int>();
        for (var i = 0; i < prefabCount; i++)
        {
            if (!source.ReadInt(out var index)) return false;
            // Out-of-range indices are dropped individually; the restriction itself survives.
            // Duplicates are left in place on purpose: they are redundant, not wrong, and the
            // dedupe pass would cost O(prefabs^2) on a record that is only bounded by a sanity cap.
            indices[i] = index;
        }

        restriction.Target = target;
        restriction.PrefabIndices = Compact(indices, data.PrefabNames.Count);
        data.Restrictions.Add(restriction);
        return true;
    }

    private static int[] Compact(int[] indices, int nameCount)
    {
        if (indices.Length == 0) return Array.Empty<int>();
        var write = 0;
        for (var read = 0; read < indices.Length; read++)
        {
            var value = indices[read];
            if (value < 0 || value >= nameCount) continue;
            indices[write++] = value;
        }
        if (write == indices.Length) return indices;
        var trimmed = new int[write];
        Array.Copy(indices, trimmed, write);
        return trimmed;
    }
}
