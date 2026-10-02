# RouteFilter save format

Current schema: **3**. Written against Cities: Skylines II 1.6.0f1.

## The one rule

**Save player intent. Do not save implementation state.**

A save records that certain vehicle prefabs are forbidden at certain targets. It does not record a
vehicle that is on its third attempt, a lease on a lane, a graph publication stage, a safety verdict
or a candidate list. All of those are derived and are rebuilt from the configuration on load.

That rule is what lets a future 3.0 replace the enforcement engine entirely without redefining what
a player's restriction means.

## Payload framing

`Colossal.Serialization.Entities.ComponentSystemSerializer<T>` wraps every system payload with an
exact byte length and then **asserts on load that the system consumed exactly that many bytes**,
throwing `ComponentSerializerException` otherwise. RouteFilter therefore always emits its own inner
length prefix with `IWriter.Begin()` / `IWriter.End(block)`, and always consumes it with
`IReader.Begin(out size)` before parsing. A corrupt body is then a skipped restriction, never a
failed city load.

## Byte layout

    [ inner block ]                     length field covers everything after itself
        uint   magic                    RFLT = 0x544C4652
        ushort schema                   3
        ushort flags                    reserved, currently 0
        int    prefabNameCount
        string prefabName               x prefabNameCount     (int length, then UTF-16 chars)
        int    restrictionCount
            byte   kind                 0 = node, 1 = segment
            int3   anchor               quantized node position / first segment endpoint
            int3   endAnchor            quantized second endpoint (segments only)
            int    lengthCentimetres    segment length in cm  (segments only)
            int    prefabCount
            int    prefabIndex          x prefabCount        (index into the name table)

### Why a name table
A hundred-target city that forbids the same thirty prefabs would otherwise repeat thirty strings a
hundred times. One deduplicated table is easier to read, diff and migrate than any binary trick, and
the size saving is not the point - clarity is.

### Why fixed-point geometry instead of an Entity
`Unity.Entities.Entity` is an archetype-local index plus version and is **not stable across
save/load**. It is kept only as a validation hint, never as identity. Identity is the target's baked
geometry, which round-trips exactly because vanilla writes the underlying floats unchanged. Positions
are stored as integers scaled by 256 (about 3.9 mm) so identity comparison is exact integer equality
rather than floating-point tolerance.

For a segment, identity is (anchor, endAnchor, lengthCentimetres). For a node it is the anchor alone.

## Target resolution at load

1. Build an anchor to node index once, storing `Entity.Null` for any anchor that occurs more than
   once, so an ambiguous position can never resolve to an arbitrary candidate.
2. Node: look up the anchor; require a unique match.
3. Segment: resolve both endpoint anchors uniquely, then walk the start node's `ConnectedEdge` buffer
   for the single edge whose other endpoint matches **and** whose curve length matches.
4. Anything else is **unresolved**: the restriction is skipped and reported.

RouteFilter never falls back to the nearest node or the nearest segment. A restriction on the wrong
intersection is worse than no restriction.

## Missing prefabs

Forbidden prefabs are identified by their stable prefab name. A name that cannot be resolved is:

- kept in the pending-restore queue for up to 600 attempts, so an asset pack that streams in late is
  still restored;
- if *some* names for a target resolve and others do not, the record waits rather than applying a
  partial list, because applying would overwrite the buffer and lose the unresolved names;
- if **none** resolve after 600 attempts, the record is dropped and reported. A restriction with no
  items is not a restriction.

RouteFilter never matches a similarly named prefab, and one missing prefab never fails a whole save.

## Failure matrix

| Condition | Behaviour |
| --- | --- |
| magic mismatch | treated as a legacy payload; if the legacy reader also refuses, enforcement stays off and persistence locks |
| schema > 3 | body copied verbatim, `PersistenceLocked` set, enforcement disabled, payload re-emitted byte for byte on every save |
| schema < 1 | corrupt; enforcement disabled, persistence locked, nothing overwritten |
| truncated header | corrupt; enforcement disabled, payload left untouched |
| truncated body | per-record isolation: the unreadable record is dropped, the rest apply |
| corrupt count field | rejected before use, so a corrupt count cannot trigger a huge allocation or loop |
| out-of-range prefab index | dropped individually; the restriction survives with fewer items |
| ambiguous target anchor | unresolved; skipped and reported, never guessed |
| legacy record count implausible | clamped, remainder ignored, reported; the reader never runs a corrupt-sized loop |

A payload whose header cannot be trusted **locks persistence**. That is deliberate: a
half-parse-and-overwrite is how a damaged save becomes a destroyed one. The player can clear it
deliberately with Reset.

## What is never written

Topology and gate records, candidate matches, safety verdicts and distributions, lane leases,
reroute attempts, cooldowns, pending restore queues, graph publication state, diagnostics, native
container contents and managed caches. All of it is rebuilt from the configuration after load.

## Status

| Item | Status |
| --- | --- |
| Byte layout, round trip, legacy V1/V2 migration, future-schema preservation, corruption containment, per-record isolation, out-of-range indices, duplicate records, quantization stability | MIGRATION VERIFIED (50 offline checks, `Tests/SaveFormatTests`) |
| Unity `IWriter`/`IReader` framing and the game's payload size assertion | BUILD VERIFIED, **NOT TESTED** in game |
| ECS entity remapping and live target resolution against a real city | **NOT TESTED** |
| Reset -> save -> reload yields zero restrictions | **NOT TESTED** |
| Active road lease -> save -> quit -> reload leaves no ghost lane state | **NOT TESTED** |
| Disable mod -> reload leaves no ghost lane state | **NOT TESTED** |
| Active rail mutation save test | structurally satisfied: the rail backend performs no `TrackLane` write |
