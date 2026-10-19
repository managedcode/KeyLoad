# ADR-060: generated internal serialization

Status: Accepted. Current owner direction: 2026-10-19.

## Decision

The official Orleans 10.4.0 generated serializer owns binary encoding, decoding,
type metadata, object references, collections and field evolution. KeyLoad has no
custom field codec, binary grammar, assembly-name selector, per-assembly service
provider cache, wire walker or replacement JSON DOM serialization model.

The shared serialization entry uses one standard `AddSerializer` registration
and native pooled sessions. `JsonElement` alone uses the official
`Microsoft.Orleans.Serialization.SystemTextJson` 10.4.0 codec; other attributed
contracts use generated binary codecs. This is an explicit type registration,
not a general-purpose JSON serializer selection rule.

Active DTOs keep their explicit `[GenerateSerializer]`, `[Alias]` and `[Id]`
contracts. The generated `RuntimePayload` application record retains its current
version and value fields. Application envelope versions, required values, exact
command root types, complete input consumption and domain/resource checks remain
KeyLoad responsibilities. Orleans owns the binary representation and supported
field evolution. No custom preflight parser interprets that representation.

## Boundaries

Public HTTP/MCP JSON, exact caller-owned document text, canonical identity hashes,
sortable keys, raw ZoneTree values and WAL framing, blob payloads, checksums,
cryptography and archive formats retain their owning contracts. Binary map output
is not a canonical identity. Unsupported application/storage versions fail before
effects. No previous-format reader or automatic data conversion is introduced.

MCP authentication uses the decoded generated `PrincipalRecord`. Existing byte
limits, ingress reservations, structural projection limits, cancellation and
fresh persisted authorization remain enforced. It does not decode Orleans fields
or construct an alternative principal from wire references.

## Acceptance

Causal server-envelope and fresh text-root receipt round trips, active DTO and
collection tests, malformed/trailing input, unsupported application versions,
command identity/accounting, signed claims and actual storage recovery remain
required. Genuine Aspire SDK/MCP operations and abrupt silo restart must run
through the published RapidMembership provider.

The deleted custom wire/DOM implementation and tests exclusive to its private
parser are retired together. Native codec behavior is verified through real
values and whole operations. Compiler/analyzer, formatting, governance, coverage,
process recovery and exact-source Linux qualification remain separate gates.
Local builds or focused passes do not establish complete product qualification,
power-loss durability, endurance or performance. Measurements retain their own
source/machine identities and never become database comparison figures.
