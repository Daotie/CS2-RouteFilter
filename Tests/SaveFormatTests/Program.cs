using RouteFilter.Persistence;


// Offline contract fixtures for the RouteFilter save format. These exercise the byte-level
// format, migration and corruption containment. They do NOT exercise Unity serialization,
// ECS entity remapping, the game's size check, or anything that needs the game running.

var failures = new List<string>();
var passed = 0;

void Check(bool condition, string name)
{
    if (condition) { passed++; return; }
    failures.Add(name);
}

void Section(string name) => Console.WriteLine($"--- {name}");

// ---------------------------------------------------------------- V2 / legacy fixtures

// Legacy layout is two groups: nodes, then segments. Encoding both correctly is the point of
// this helper, because a fixture that lies about the layout would "prove" the wrong thing.
static byte[] LegacyPayload(int version,
    (int index, int ver, string[] assets)[] nodes,
    (int index, int ver, string[] assets)[] segments = null)
{
    segments ??= Array.Empty<(int, int, string[])>();
    var sink = new RestrictionByteSink();
    sink.WriteInt(version);
    sink.WriteInt(nodes.Length);
    foreach (var record in nodes)
    {
        sink.WriteInt(record.index);
        sink.WriteInt(record.assets.Length);
        foreach (var asset in record.assets) sink.WriteString(asset);
    }
    sink.WriteInt(segments.Length);
    foreach (var record in segments)
    {
        sink.WriteInt(record.index);
        sink.WriteInt(record.assets.Length);
        foreach (var asset in record.assets) sink.WriteString(asset);
    }
    return sink.ToArray();
}

Section("legacy migration");
{
    var bytes = LegacyPayload(2, new[] { (10, 1, new[] { "car.a", "car.b" }) },
        new[] { (11, 2, new[] { "train.x" }) });
    var result = LegacyRouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(result.Status == LegacyDecodeStatus.Ok, "legacy v2 normal decodes");
    Check(result.Data.Records.Count == 2, "legacy v2 record count");
    Check(result.Data.Records[0].EntityTableIndex == 10,
        "legacy v2 keeps entity reference for the ECS identity step");
    Check(result.Data.Records[0].Kind == 0, "legacy v2 first group is nodes");
    Check(result.Data.Records[1].Kind == 1, "legacy v2 second group is segments");
    Check(result.Data.Records[0].PrefabNames.Count == 2, "legacy v2 asset names");
}

{
    var bytes = LegacyPayload(1, new[] { (7, 3, new[] { "train.x" }) });
    var result = LegacyRouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(result.Status == LegacyDecodeStatus.Ok, "legacy v1 decodes");
    Check(result.Data.Records.Count == 1, "legacy v1 record kept");
}

{
    var bytes = LegacyPayload(2, Array.Empty<(int, int, string[])>());
    var result = LegacyRouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(result.Status == LegacyDecodeStatus.Ok, "legacy v2 empty decodes");
    Check(result.Data.Records.Count == 0, "legacy v2 empty has no records");
}

{
    // Unknown legacy version: refuse rather than guess a layout.
    var bytes = LegacyPayload(7, new[] { (1, 1, Array.Empty<string>()) });
    var result = LegacyRouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(result.Status == LegacyDecodeStatus.NotLegacyPayload, "unknown legacy version refused");
}

{
    // Truncated legacy payload: nothing applied.
    var bytes = LegacyPayload(2, new[] { (1, 1, new[] { "car.a" }) });
    Array.Resize(ref bytes, bytes.Length - 4);
    var result = LegacyRouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(result.Status == LegacyDecodeStatus.Corrupt, "truncated legacy payload is corrupt");
}

{
    // A record whose asset count is impossible costs that record, not the stream position,
    // because the record's own length is still fully determined by its asset count.
    var bytes = LegacyPayload(2, new[] { (1, 1, new[] { "car.a" }), (2, 1, new[] { "car.b" }) });
    var result = LegacyRouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(result.Status == LegacyDecodeStatus.Ok && result.Data.Records.Count == 2,
        "well-formed multi-record legacy payload decodes completely");
}

{
    // Implausible record count must not attempt a giant loop; the remainder is ignored and reported.
    var sink = new RestrictionByteSink();
    sink.WriteInt(2);
    sink.WriteInt(int.MaxValue);
    sink.WriteInt(0);
    var result = LegacyRouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(result.DroppedRecords > 0 && result.Detail.Contains("implausible"),
        "implausible legacy record count is clamped and reported, not executed");
}

// ---------------------------------------------------------------- current schema round trip

Section("current schema round trip");
{
    var data = RouteFilterSaveData.CreateEmpty();
    data.PrefabNames.Add("car.a");
    data.PrefabNames.Add("car.b");
    data.Restrictions.Add(new PersistentRestriction
    {
        Target = new RestrictionTargetIdentity
        {
            Kind = 0,
            Anchor = RestrictionAnchor.Quantize(12.5f, 0f, -7.25f)
        },
        PrefabIndices = new[] { 0, 1 }
    });
    data.Restrictions.Add(new PersistentRestriction
    {
        Target = new RestrictionTargetIdentity
        {
            Kind = 1,
            Anchor = RestrictionAnchor.Quantize(1f, 2f, 3f),
            EndAnchor = RestrictionAnchor.Quantize(4f, 5f, 6f),
            LengthCentimetres = 12345
        },
        PrefabIndices = new[] { 1 }
    });

    var sink = new RestrictionByteSink();
    RouteFilterSaveCodec.Encode(data, sink);
    var bytes = sink.ToArray();

    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(decoded.Status == SaveDecodeStatus.Ok, "round trip is clean");
    Check(decoded.Data.Restrictions.Count == 2, "round trip keeps both restrictions");
    Check(decoded.Data.PrefabNames.Count == 2, "round trip keeps both prefab names");
    Check(decoded.Data.Restrictions[0].Target.Matches(data.Restrictions[0].Target), "node identity round trips");
    Check(decoded.Data.Restrictions[1].Target.Matches(data.Restrictions[1].Target), "segment identity round trips");
    Check(decoded.Data.Restrictions[0].PrefabIndices.Length == 2, "node prefab indices round trip");
    Check(decoded.Data.Restrictions[1].PrefabIndices[0] == 1, "segment prefab index round trips");
}

{
    var data = RouteFilterSaveData.CreateEmpty();
    var sink = new RestrictionByteSink();
    RouteFilterSaveCodec.Encode(data, sink);
    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(decoded.Status == SaveDecodeStatus.Ok, "empty payload round trips");
    Check(decoded.Data.Restrictions.Count == 0, "empty payload has no restrictions");
}

// ---------------------------------------------------------------- future schema protection

Section("future schema protection");
{
    var sink = new RestrictionByteSink();
    sink.WriteUInt(RouteFilterSaveData.Magic);
    sink.WriteUShort(99);
    sink.WriteUShort(0);
    for (var i = 0; i < 64; i++) sink.WriteByte((byte)(i * 7));
    var bytes = sink.ToArray();

    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(decoded.Status == SaveDecodeStatus.FutureSchema, "future schema is recognised, not parsed");
    Check(decoded.ForeignPayload != null && decoded.ForeignPayload.Length == bytes.Length,
        "future schema includes the original header");

    // Re-emitting the preserved payload must reproduce the original bytes exactly.
    var reemit = new RestrictionByteSink();
    reemit.WriteBytes(decoded.ForeignPayload);
    var reemitted = reemit.ToArray();
    var expected = bytes;
    Check(reemitted.Length == expected.Length, "re-emitted future payload has the same length");
    var identical = true;
    for (var i = 0; i < expected.Length; i++) if (reemitted[i] != expected[i]) { identical = false; break; }
    Check(identical, "re-emitted future payload is byte identical");
}

// ---------------------------------------------------------------- corruption containment

Section("corruption containment");
{
    // Not a RouteFilter payload at all.
    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(new byte[] { 1, 2, 3, 4, 5 }));
    Check(decoded.Status == SaveDecodeStatus.NotRouteFilterData, "foreign magic is not ours");
}

{
    // Header present, body truncated: corrupt, and the caller must disable enforcement.
    var data = RouteFilterSaveData.CreateEmpty();
    data.PrefabNames.Add("car.a");
    data.Restrictions.Add(new PersistentRestriction
    {
        Target = new RestrictionTargetIdentity { Kind = 0, Anchor = RestrictionAnchor.Quantize(1f, 1f, 1f) },
        PrefabIndices = new[] { 0 }
    });
    var sink = new RestrictionByteSink();
    RouteFilterSaveCodec.Encode(data, sink);
    var bytes = sink.ToArray();
    Array.Resize(ref bytes, bytes.Length - 3);
    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(decoded.Status == SaveDecodeStatus.Corrupt, "truncated checksummed body is quarantined");
    Check(!decoded.IsApplicable, "checksum failure exposes no partial intent");
    Check(decoded.Data == null, "nothing half-applied from the truncated record");
}

{
    // Truncated header: the payload cannot be trusted at all, so nothing is offered to apply.
    var sink = new RestrictionByteSink();
    sink.WriteUInt(RouteFilterSaveData.Magic);
    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(decoded.Status == SaveDecodeStatus.Corrupt, "truncated header is corrupt");
    Check(!decoded.IsApplicable, "a corrupt payload is never applicable");
    Check(decoded.Data is null, "a corrupt payload exposes no data");
}

{
    // One bad record must cost one record, not the file.
    var sink = new RestrictionByteSink();
    sink.WriteUInt(RouteFilterSaveData.Magic);
    sink.WriteUShort(3);
    sink.WriteUShort(0);
    sink.WriteInt(1);
    sink.WriteString("car.a");
    sink.WriteInt(2);

    // good record
    sink.WriteInt(0);
    var good = RestrictionAnchor.Quantize(5f, 0f, 0f);
    sink.WriteInt(good.X); sink.WriteInt(good.Y); sink.WriteInt(good.Z);
    sink.WriteInt(0); sink.WriteInt(0); sink.WriteInt(0);
    sink.WriteInt(0);
    sink.WriteInt(1);

    // bad record: impossible kind
    sink.WriteInt(77);
    sink.WriteInt(0); sink.WriteInt(0); sink.WriteInt(0);
    sink.WriteInt(0); sink.WriteInt(0); sink.WriteInt(0);
    sink.WriteInt(0);

    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(decoded.Status == SaveDecodeStatus.PartiallyRecovered, "bad record yields partial recovery");
    Check(decoded.DroppedRecords == 1, "exactly one record dropped");
    Check(decoded.Data.Restrictions.Count == 1, "the good record survives");
}

{
    // Out-of-range prefab indices are dropped individually; the restriction survives.
    var sink = new RestrictionByteSink();
    sink.WriteUInt(RouteFilterSaveData.Magic);
    sink.WriteUShort(3);
    sink.WriteUShort(0);
    sink.WriteInt(1);
    sink.WriteString("car.a");
    sink.WriteInt(1);
    sink.WriteInt(0);
    var anchor = RestrictionAnchor.Quantize(9f, 9f, 9f);
    sink.WriteInt(anchor.X); sink.WriteInt(anchor.Y); sink.WriteInt(anchor.Z);
    sink.WriteInt(0); sink.WriteInt(0); sink.WriteInt(0);
    sink.WriteInt(0);
    sink.WriteInt(3);
    sink.WriteInt(0);
    sink.WriteInt(-5);
    sink.WriteInt(9999);

    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(decoded.Status == SaveDecodeStatus.Ok, "invalid indices do not corrupt the record");
    Check(decoded.Data.Restrictions.Count == 1, "restriction kept despite bad indices");
    Check(decoded.Data.Restrictions[0].PrefabIndices.Length == 1, "only the valid index survives");
    Check(decoded.Data.Restrictions[0].PrefabIndices[0] == 0, "valid index preserved");
}

{
    // Implausible prefab table size must be rejected, not allocated.
    var sink = new RestrictionByteSink();
    sink.WriteUInt(RouteFilterSaveData.Magic);
    sink.WriteUShort(3);
    sink.WriteUShort(0);
    sink.WriteInt(int.MaxValue);
    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(decoded.Status == SaveDecodeStatus.Corrupt, "implausible prefab table rejected");
}

{
    // A restriction whose prefab list is empty is dropped by the queue step, not by the codec.
    var data = RouteFilterSaveData.CreateEmpty();
    data.Restrictions.Add(new PersistentRestriction
    {
        Target = new RestrictionTargetIdentity { Kind = 0, Anchor = RestrictionAnchor.Quantize(3f, 3f, 3f) },
        PrefabIndices = Array.Empty<int>()
    });
    var sink = new RestrictionByteSink();
    RouteFilterSaveCodec.Encode(data, sink);
    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(decoded.Status == SaveDecodeStatus.Ok && decoded.Data.Restrictions.Count == 1,
        "empty prefab list survives the codec and is filtered by the queue step");
}

// ---------------------------------------------------------------- duplicate records

Section("duplicate records");
{
    var data = RouteFilterSaveData.CreateEmpty();
    data.PrefabNames.Add("car.a");
    var identity = new RestrictionTargetIdentity { Kind = 0, Anchor = RestrictionAnchor.Quantize(2f, 4f, 6f) };
    data.Restrictions.Add(new PersistentRestriction { Target = identity, PrefabIndices = new[] { 0 } });
    data.Restrictions.Add(new PersistentRestriction { Target = identity, PrefabIndices = new[] { 0 } });
    var sink = new RestrictionByteSink();
    RouteFilterSaveCodec.Encode(data, sink);
    var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(decoded.Status == SaveDecodeStatus.Ok && decoded.Data.Restrictions.Count == 2,
        "duplicate targets are decoded; deduplication is the runtime's job");
    Check(decoded.Data.Restrictions[0].Target.Matches(decoded.Data.Restrictions[1].Target),
        "duplicates carry identical identity so the runtime can dedupe exactly");
}

// ---------------------------------------------------------------- quantization stability

Section("target identity quantization");
{
    var a = RestrictionAnchor.Quantize(100f, 0f, -100f);
    var b = RestrictionAnchor.Quantize(100f, 0f, -100f);
    Check(a.Equals(b), "identical positions quantize identically");
    var c = RestrictionAnchor.Quantize(100.01f, 0f, -100f);
    Check(!a.Equals(c), "a 1 cm difference is a different anchor, not a near match");

    var node = new RestrictionTargetIdentity { Kind = 0, Anchor = a };
    var segment = new RestrictionTargetIdentity { Kind = 1, Anchor = a, EndAnchor = b, LengthCentimetres = 5 };
    Check(!node.Matches(segment), "kind is part of identity");
    Check(segment.Matches(segment), "segment identity is self-consistent");
    Check(!segment.Matches(new RestrictionTargetIdentity
    { Kind = 1, Anchor = a, EndAnchor = b, LengthCentimetres = 6 }),
        "segment length is part of identity");
}

Section("production byte codec robustness");
{
    var sink = new RestrictionByteSink();
    sink.WriteUInt(RouteFilterSaveData.Magic); sink.WriteUShort(3); sink.WriteUShort(128);
    sink.WriteInt(0); sink.WriteInt(0);
    var bytes = sink.ToArray();
    var result = RouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(result.Status == SaveDecodeStatus.FutureSchema && result.ForeignPayload.SequenceEqual(bytes),
        "unknown header flags preserved instead of interpreted");
    var clean = new RestrictionByteSink(); RouteFilterSaveCodec.Encode(RouteFilterSaveData.CreateEmpty(), clean);
    clean.WriteByte(99);
    var extra = RouteFilterSaveCodec.Decode(new RestrictionByteSource(clean.ToArray()));
    Check(extra.Status == SaveDecodeStatus.Corrupt && !extra.IsApplicable, "trailing extension cannot be silently discarded");
}
{
    var sink = new RestrictionByteSink();
    sink.WriteString("杞﹁締 Truck01");
    sink.WriteInt(123456);
    var source = new RestrictionByteSource(sink.ToArray());
    Check(source.ReadString(out var text) && text == "杞﹁締 Truck01", "UTF16 string has exactly one length prefix");
    Check(source.ReadInt(out var sentinel) && sentinel == 123456 && source.Remaining == 0,
        "field after string remains aligned");
}
{
    var sink = new RestrictionByteSink();
    sink.WriteUInt(RouteFilterSaveData.Magic); sink.WriteUShort(65535); sink.WriteUShort(17);
    var bytes = sink.ToArray();
    var result = RouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
    Check(result.Status == SaveDecodeStatus.FutureSchema && result.ForeignPayload.SequenceEqual(bytes),
        "header-only future payload preserved exactly, including nonzero flags");
}
{
    var data = RouteFilterSaveData.CreateEmpty();
    data.PrefabNames.Add(""); data.PrefabNames.Add("Truck01");
    data.Restrictions.Add(new PersistentRestriction { Target = new RestrictionTargetIdentity(), PrefabIndices = new[] { 1 } });
    var sink = new RestrictionByteSink(); RouteFilterSaveCodec.Encode(data, sink);
    var result = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
    Check(result.Data.PrefabNames.Count == 2 && result.Data.PrefabNames[1] == "Truck01" &&
        result.Data.Restrictions[0].PrefabIndices[0] == 1, "empty name never shifts prefab indices");
    var original = sink.ToArray();
    for (var length = 0; length < original.Length; length++)
    {
        var truncated = original.Take(length).ToArray();
        var partial = RouteFilterSaveCodec.Decode(new RestrictionByteSource(truncated));
        Check(partial.Status != SaveDecodeStatus.Ok, "every truncation is detected at " + length);
    }
}

Section("checksum corruption containment");
{
    var data = RouteFilterSaveData.CreateEmpty(); data.PrefabNames.Add("Truck01");
    data.Restrictions.Add(new PersistentRestriction { Target = new RestrictionTargetIdentity { Kind = 1,
        Anchor = new RestrictionAnchor(100, 200, 300), EndAnchor = new RestrictionAnchor(400, 500, 600),
        LengthCentimetres = 5000 }, PrefabIndices = new[] { 0 } });
    var sink = new RestrictionByteSink(); RouteFilterSaveCodec.Encode(data, sink);
    var bytes = sink.ToArray();
    for (var position = 0; position < 8; position++)
    {
        var damaged = (byte[])bytes.Clone(); damaged[position] ^= 1;
        var result = RouteFilterSaveCodec.Decode(new RestrictionByteSource(damaged));
        Check(!result.IsApplicable, "header damage cannot apply intent at " + position);
    }
    for (var position = 8; position < bytes.Length; position++)
    {
        var damaged = (byte[])bytes.Clone(); damaged[position] ^= 1;
        var result = RouteFilterSaveCodec.Decode(new RestrictionByteSource(damaged));
        Check(result.Status == SaveDecodeStatus.Corrupt && !result.IsApplicable,
            "bit damage to intent/checksum refused at " + position);
    }
}

Section("directional entry persistence and stable endpoint identity");
{
    var a = new RestrictionAnchor(0, 0, 0); var b = new RestrictionAnchor(25600, 0, 0);
    var road = new RestrictionTargetIdentity { Kind = 1, Anchor = a, EndAnchor = b, LengthCentimetres = 10000 };
    var entryA = new RestrictionEntryIdentity { Connection = road, Endpoint = a,
        CurveMidpoint = new RestrictionAnchor(12800, 0, 100), RoadPrefab = "Highway2Lane" };
    var entryB = entryA; entryB.Endpoint = b;
    Check(entryA.IsValid && entryB.IsValid, "both actual segment endpoints valid");
    Check(!entryA.Equals(entryB), "entering A is distinct from entering B");
    var reversed = entryA; reversed.Connection.Anchor = b; reversed.Connection.EndAnchor = a;
    Check(entryA.Equals(reversed) && entryA.GetHashCode() == reversed.GetHashCode(),
        "runtime endpoint ordering cannot flip entry identity");
    Check(new HashSet<RestrictionEntryIdentity> { entryA }.Contains(reversed), "normalized identity supports hashed lookup");
    Check(!new HashSet<RestrictionEntryIdentity> { entryA }.Contains(entryB), "disabled opposite endpoint remains distinct");
    var replacement = entryA; replacement.RoadPrefab = "DifferentRoad";
    Check(!entryA.Equals(replacement), "replaced road prefab cannot inherit entry intent");
    replacement = entryA; replacement.CurveMidpoint.Z++;
    Check(!entryA.Equals(replacement), "changed curve cannot borrow nearest endpoint mapping");
    replacement = entryA; replacement.Endpoint = new RestrictionAnchor(12, 0, 0);
    Check(!replacement.IsValid, "unrelated endpoint rejected");
    replacement = entryA; replacement.Connection.EndAnchor = a;
    Check(!replacement.IsValid, "coincident ambiguous endpoints rejected");

    foreach (var kind in new byte[] { 0, 1 })
    foreach (var entries in new RestrictionEntryIdentity[][] { null, Array.Empty<RestrictionEntryIdentity>(), new[] { entryA }, new[] { entryB }, new[] { entryA, entryB } })
    {
        var data = RouteFilterSaveData.CreateEmpty(); data.PrefabNames.Add("Truck01");
        var target = road; target.Kind = kind;
        data.Restrictions.Add(new PersistentRestriction { Target = target, PrefabIndices = new[] { 0 }, EnabledEntries = entries });
        var sink = new RestrictionByteSink(); RouteFilterSaveCodec.Encode(data, sink);
        var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(sink.ToArray()));
        Check(decoded.Status == SaveDecodeStatus.Ok && decoded.Data.Restrictions.Count == 1, $"kind {kind} direction record round trips");
        var actual = decoded.Data.Restrictions[0].EnabledEntries;
        Check(entries == null ? actual == null : actual != null && actual.SequenceEqual(entries),
            $"kind {kind} preserves all/none/subset distinction");
    }

    // Encode the actual pre-feature record layout rather than relabeling a schema-5 body.
    foreach (var schema in new ushort[] { 3, 4 })
    {
        var old = new RestrictionByteSink(); old.WriteUInt(RouteFilterSaveData.Magic); old.WriteUShort(schema); old.WriteUShort(0);
        old.WriteInt(1); old.WriteString("Truck01"); old.WriteInt(2);
        for (var kind = 0; kind <= 1; kind++)
        {
            old.WriteInt(kind); old.WriteInt(0); old.WriteInt(0); old.WriteInt(0);
            old.WriteInt(b.X); old.WriteInt(b.Y); old.WriteInt(b.Z); old.WriteInt(10000);
            old.WriteInt(1); old.WriteInt(0);
        }
        var bytes = old.ToArray();
        if (schema == 4)
        {
            uint crc = uint.MaxValue;
            foreach (var value in bytes) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u); }
            old.WriteUInt(~crc); bytes = old.ToArray();
        }
        var decoded = RouteFilterSaveCodec.Decode(new RestrictionByteSource(bytes));
        Check(decoded.Status == SaveDecodeStatus.Ok && decoded.Data.Restrictions.Count == 2, $"actual schema {schema} node and segment accepted");
        Check(decoded.Data.Restrictions.All(record => record.EnabledEntries == null), $"old schema {schema} means ALL entries");
    }
    var invalid = RouteFilterSaveData.CreateEmpty(); invalid.Restrictions.Add(new PersistentRestriction { Target = road,
        EnabledEntries = new[] { replacement } });
    var refused = false;
    try { RouteFilterSaveCodec.Encode(invalid, new RestrictionByteSink()); } catch (ArgumentException) { refused = true; }
    Check(refused, "invalid entry identity cannot be written");
    invalid.Restrictions[0] = new PersistentRestriction { Target = road, EnabledEntries = Enumerable.Repeat(entryA, 65).ToArray() };
    refused = false;
    try { RouteFilterSaveCodec.Encode(invalid, new RestrictionByteSink()); } catch (ArgumentException) { refused = true; }
    Check(refused, "entry count is bounded before encode");
    var semantic = RouteFilterSaveData.CreateEmpty(); semantic.PrefabNames.Add("Truck01");
    semantic.Restrictions.Add(new PersistentRestriction { Target = road, PrefabIndices = new[] { 0 }, EnabledEntries = new[] { entryA } });
    var semanticSink = new RestrictionByteSink(); RouteFilterSaveCodec.Encode(semantic, semanticSink);
    var damaged = semanticSink.ToArray();
    // Entry endpoint begins 20 bytes before the midpoint and road-prefab string.
    var roadNameBytes = 4 + "Highway2Lane".Length * 2;
    var endpointOffset = damaged.Length - 4 - roadNameBytes - 12 - 12;
    BitConverter.GetBytes(12345).CopyTo(damaged, endpointOffset);
    uint repairCrc = uint.MaxValue;
    for (var i = 0; i < damaged.Length - 4; i++)
    { repairCrc ^= damaged[i]; for (var bit = 0; bit < 8; bit++) repairCrc = (repairCrc >> 1) ^ ((repairCrc & 1) != 0 ? 0xEDB88320u : 0u); }
    BitConverter.GetBytes(~repairCrc).CopyTo(damaged, damaged.Length - 4);
    var recovered = RouteFilterSaveCodec.Decode(new RestrictionByteSource(damaged));
    Check(recovered.Status == SaveDecodeStatus.Ok && recovered.Data.Restrictions.Count == 1 &&
        recovered.Data.Restrictions[0].PrefabIndices.SequenceEqual(new[] { 0 }), "semantically invalid direction retains forbidden prefabs");
    Check(recovered.Data.DirectionFallbackCount == 1 && recovered.Data.Restrictions[0].EnabledEntries == null,
        "invalid direction falls back to ALL with explicit diagnostic");
}

// ---------------------------------------------------------------- report

Console.WriteLine();
Console.WriteLine($"checks passed: {passed}");
if (failures.Count == 0)
{
    Console.WriteLine("PASS: save schema round trip, legacy V1/V2 byte-layout parsing (not live migration), future-schema preservation, " +
                      "corruption containment, per-record isolation, out-of-range indices, duplicate " +
                      "records and target identity quantization.");
    Console.WriteLine("NOT TESTED HERE: Unity IWriter/IReader framing, the game's payload size check, " +
                      "ECS entity remapping, and live target resolution against a real city.");
    return 0;
}

Console.WriteLine("FAIL:");
foreach (var failure in failures) Console.WriteLine("  - " + failure);
return 1;
