# RouteFilter migration

## Pipeline

    legacy bytes (schema 1 or 2)
        -> LegacyRouteFilterSaveCodec.Decode        pure, byte level, offline testable
        -> LegacyRestrictionRecord[]                intermediate normalised model
        -> ECS identity resolution                  the only stage that needs the game
        -> RouteFilterSaveData (schema 3)           pure

    current bytes (schema 3)
        -> RouteFilterSaveCodec.Decode              pure, byte level, offline testable
        -> RouteFilterSaveData
        -> ECS identity resolution
        -> live restrictions

The two byte-level stages are separated from the identity stage on purpose: target identity cannot be
derived without live scene geometry, but everything around it is verifiable without launching the
game, and that is where migration bugs live.

## Migration is read-only with respect to the world

During migration RouteFilter does **not**:

- modify a `CarLane` or a `TrackLane`;
- set `PathOwner` flags or add `Updated`;
- create a lease or an attempt;
- invalidate or republish any graph data.

Migration only reads the configuration and writes it back. Enforcement state is created later, by the
backends, from the restored configuration, under their own rules.

## Fixtures

`Tests/SaveFormatTests` (50 checks, no Unity dependency) covers:

| Fixture | Asserted |
| --- | --- |
| V2 normal | both groups decode, entity reference and asset names preserved, kinds correct |
| V1 normal | decodes, record kept |
| V2 empty | decodes with zero records |
| V2 truncated | corrupt, nothing applied |
| V2 unknown version | refused as legacy rather than guessed |
| V2 implausible record count | clamped and reported, never executed |
| schema 3 round trip, node and segment | identity and prefab indices survive exactly |
| schema 3 empty | round trips |
| future schema (99) | recognised, not parsed, body preserved byte for byte |
| future schema re-emission | byte identical to the original body |
| foreign magic | reported as not-ours |
| truncated header | corrupt, no data exposed, not applicable |
| truncated body | partial recovery, exactly one record dropped, nothing half-applied |
| bad record kind | one record isolated, the good one survives |
| out-of-range prefab indices | dropped individually, restriction survives |
| implausible prefab table | rejected before allocation |
| empty prefab list | survives the codec, filtered by the queue step |
| duplicate records | decoded, deduplication is the runtime's job |
| quantization | identical positions match, a 1 cm difference does not |

`Tests/LeaseRules` covers the pure enforcement rules: all 65,536 lane ownership pairs, vanilla's
empty-interval representation, absolute expiry and frame wrap, acquire-only-on-empty, reroute
admission against every `PathFlags` combination that matters, emergency exemption, fixed-route
refusal, and the distance model's monotonicity in the latency budget.

Run both:

    dotnet run --project Tests/SaveFormatTests
    dotnet run --project Tests/LeaseRules

## Upgrade contract

The goal is **not** a promise that migration is bug-free. The goal is an architectural guarantee
about what a migration failure can cost:

> If migration fails, the worst case is that one restriction - or all restrictions - stop working.
> The city save is not ruined, no vehicle is permanently stopped, and no graph state is left
> permanently contaminated.

That guarantee comes from four properties, not from care:

1. **Runtime state is never persisted.** Nothing to migrate means nothing to mis-migrate.
2. **Owned mutations are released before the serializer runs**, and release is an exact
   compare-before-restore, so a city saved with RouteFilter active contains no RouteFilter mutation.
3. **A target that cannot be resolved exactly is skipped**, never guessed at. A missing restriction
   is a visible, recoverable inconvenience; a restriction on the wrong intersection is a bug the
   player cannot see.
4. **An untrusted payload disables enforcement and locks persistence**, so the original bytes are
   never overwritten by a half-understood version of themselves.

Fail-safe is preferred over preserving every restriction. That trade is intentional and permanent.

## 1.x

The 1.x enforcement systems and their runtime components are removed. They were never
`ISerializable`, so no 1.x save ever carried them and no compatibility code is required.

RouteFilter does **not** run a startup full-city scan to clean up historical 1.x pollution. Historical
contamination is handled by the standalone `RouteFilterCleanup` mod, because a per-load city-wide
scan in the main mod would reintroduce the performance, safety and ownership risks that the 2.0
rewrite exists to remove.

## Status

| Item | Status |
| --- | --- |
| V1/V2 -> intermediate -> schema 3 pipeline | IMPLEMENTED, MIGRATION VERIFIED (offline) |
| Corruption containment, future-schema protection, missing prefab, missing/ambiguous target | MIGRATION VERIFIED (offline) |
| Unknown-future-schema payload preserved byte for byte | MIGRATION VERIFIED (offline) |
| Migration performs no lane/path mutation | STATICALLY VERIFIED |
| Migration against a real 1.x save file in game | **NOT TESTED** |
| 1.x -> 2.0 in-game upgrade with a real city | **NOT TESTED** |
