# InternalSerialization

The official Orleans 10.4.2 generated serializer encodes owned internal DTOs.
[ADR-060](../ADR/ADR-060-internal-serialization.md) defines the current contract.
Public HTTP/MCP JSON, exact user content, sortable keys, opaque ZoneTree bytes,
canonical hashes and blob payloads retain their concrete owning boundaries.

## Ownership

Abstractions supplies attributed contracts, application envelope validation and
counting destinations for native encoding. Orleans owns codecs, type metadata,
collections, references and field evolution. The standalone serializer uses one
standard registration and native sessions. There is no custom serializer assembly
selection, per-assembly provider cache, wire walker or field inspection codec.
`JsonElement` uses the official Orleans SystemTextJson codec, without a custom DOM
surrogate. All other active attributed DTOs use generated binary codecs.

Core owns model validation, canonical identity and persisted command outcomes.
In-memory command snapshots use the native Orleans generated deep copier, with
contract validation before and after copying. Commands and grain requests carry
typed operation values. Replies carry a generated `GrainValue` directly; their
callers do not encode and decode a private byte payload inside an Orleans call.
Counting destinations enforce the existing byte ceilings without retaining a
second encoded reply. Mutable operation values and metadata remain independent
of the caller. Replaced binary fields are retired and typed replacements use
unused field IDs; aliases and application versions are unchanged.
Only `DatabaseCommand.Input` marks the public-input element policy, so rejected
public null elements can reach domain validation and remain in original command
outcomes. Other members retain strict validation, including required references
and initialized collections. Request admission enforces the real HTTP/MCP frame
and native Orleans message ceilings; the removed private encoded-operation
buffer has no independent size ceiling.
Storage.ZoneTree owns physical metadata, WAL and file recovery. Orleans carries
grain calls. ClientApi owns actual public JSON and authorized admission. MCP
checks its actual typed principal, encoded byte ceiling and projection limits
rather than parsing Orleans wire fields. Administrator authentication carries
the principal directly instead of a nested encoded principal buffer.

## Requirements

REQ-IS-001..009 / AC-IS-001..009 retain active attributed DTOs and stable aliases,
required domain values, native pooled serialization, exact byte accounting,
versioned application/storage metadata, bounded payloads, typed grain contracts,
signed claims, unsupported-version refusal and exact-source qualification.
Native Orleans owns its supported field evolution; KeyLoad adds no private wire
grammar. Application validation is required before effects.

The [acceptance matrix](InternalSerialization/Acceptance.md) identifies current
operation-level verification. Source/build checks, focused local tests, real
process recovery, Aspire SDK/MCP, coverage and delivered-source Linux execution
are separate gates. No power-loss, endurance or speed claim follows from a local
round trip. [Serialization diagnostics](BenchmarkComparisons/OrleansSerialization.md)
remain separate from end-to-end database comparisons.

## Concrete contracts

ReceiveAcrossLanes remains an Orleans parent orchestration capability. Only its
individual Receive leaves are canonical Core commands; unsupported generic
commands fail before effects. Active generated aliases and field IDs remain
stable. Exact user text, canonical operation fingerprints, current persisted
authority and joined cleanup stay owned by their actual features.
