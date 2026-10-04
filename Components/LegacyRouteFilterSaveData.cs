using System.Collections.Generic;

namespace RouteFilter.Persistence;

/// <summary>
/// Normalised intermediate model shared by every legacy payload version.
///
/// This is deliberately not the current schema. The legacy payloads stored raw
/// <c>Unity.Entities.Entity</c> references, and turning one of those back into a stable target
/// identity needs live ECS geometry. Keeping that conversion outside the format layer means the
/// migration pipeline is: legacy bytes -> this model -> (ECS identity resolution) -> current
/// schema, and the byte-level half stays verifiable offline.
/// </summary>
public sealed class LegacyRestrictionRecord
{
    public int EntityTableIndex;
    /// <summary>0 = node, 1 = segment, as written by the legacy writer.</summary>
    public byte Kind;
    public readonly List<string> PrefabNames = new();
}

public sealed class LegacySaveData
{
    public int Version;
    public readonly List<LegacyRestrictionRecord> Records = new();
}

public enum LegacyDecodeStatus
{
    Ok = 0,
    /// <summary>First integer was not a legacy version this build knows how to read.</summary>
    NotLegacyPayload = 1,
    /// <summary>Version recognised, body not. Nothing is applied.</summary>
    Corrupt = 2
}

public sealed class LegacyDecodeResult
{
    public LegacyDecodeStatus Status = LegacyDecodeStatus.Ok;
    public LegacySaveData Data;
    public int DroppedRecords;
    public string Detail = string.Empty;
}

/// <summary>
/// Reader for the 1.x / early-2.0 payload:
/// <code>
/// int      version                    1 or 2
/// int      nodeCount
///   { Entity target; int assetCount; string[assetCount] }  x nodeCount
/// int      segmentCount
///   { Entity target; int assetCount; string[assetCount] }  x segmentCount
/// </code>
///
/// Every count is clamped before use and every record is validated independently, so a single bad
/// record costs that record. The version int is the only field trusted before validation, and a
/// value outside the supported set aborts the whole legacy read rather than guessing.
/// </summary>
public static class LegacyRouteFilterSaveCodec
{
    public const int NewestLegacyVersion = 2;
    private const int MaxLegacyRecordCount = 200_000;

    public static LegacyDecodeResult Decode(IRestrictionSaveSource source)
    {
        var result = new LegacyDecodeResult();
        if (source == null) return Fail(result, LegacyDecodeStatus.NotLegacyPayload, "no source");

        if (!source.ReadInt(out var version))
            return Fail(result, LegacyDecodeStatus.NotLegacyPayload, "payload shorter than the version field");
        if (version < 1 || version > NewestLegacyVersion)
            return Fail(result, LegacyDecodeStatus.NotLegacyPayload, $"unsupported legacy version {version}");

        var data = new LegacySaveData { Version = version };
        result.Data = data;

        if (!source.ReadInt(out var nodeCount))
            return Fail(result, LegacyDecodeStatus.Corrupt, "truncated node count");
        ReadGroup(source, data, nodeCount, 0, result);
        if (result.Status == LegacyDecodeStatus.Corrupt) return result;

        if (!source.ReadInt(out var segmentCount))
            return Fail(result, LegacyDecodeStatus.Corrupt, "truncated segment count");
        ReadGroup(source, data, segmentCount, 1, result);
        if (result.Status == LegacyDecodeStatus.Corrupt) return result;

        result.Status = LegacyDecodeStatus.Ok;
        result.Detail += $"legacy v{version}: {data.Records.Count} records, {result.DroppedRecords} dropped";
        return result;
    }

    private static void ReadGroup(
        IRestrictionSaveSource source,
        LegacySaveData data,
        int count,
        byte kind,
        LegacyDecodeResult result)
    {
        if (count < 0 || count > MaxLegacyRecordCount)
        {
            // The stream cannot be resynchronised from here, so stop rather than misparse.
            result.Status = LegacyDecodeStatus.Corrupt;
            result.DroppedRecords += count < 0 ? 0 : count;
            result.Detail += $"; implausible {kind} record count {count}, remainder ignored";
            source.SkipToEnd();
            return;
        }

        for (var i = 0; i < count; i++)
        {
            if (!TryReadRecord(source, kind, out var record))
            {
                result.Status = LegacyDecodeStatus.Corrupt;
                result.DroppedRecords++;
                source.SkipToEnd();
                return;
            }
            data.Records.Add(record);
        }
    }

    private static bool TryReadRecord(IRestrictionSaveSource source, byte kind, out LegacyRestrictionRecord record)
    {
        record = null;
        if (!source.ReadInt(out var index)) return false;
        if (!source.ReadInt(out var assetCount)) return false;
        if (assetCount < 0 || assetCount > RouteFilterSaveData.MaxPrefabsPerRestriction) return false;

        var parsed = new LegacyRestrictionRecord
        {
            EntityTableIndex = index,
            Kind = kind
        };
        for (var i = 0; i < assetCount; i++)
        {
            if (!source.ReadString(out var name)) return false;
            if (string.IsNullOrEmpty(name)) continue;
            if (name.Length > RouteFilterSaveData.MaxPrefabNameLength) continue;
            parsed.PrefabNames.Add(name);
        }
        record = parsed;
        return true;
    }

    private static LegacyDecodeResult Fail(LegacyDecodeResult result, LegacyDecodeStatus status, string detail)
    {
        result.Status = status;
        // Append, never replace: the reason the reader stopped is often the reason that matters.
        result.Detail = result.Detail.Length == 0 ? detail : result.Detail + "; " + detail;
        return result;
    }
}
