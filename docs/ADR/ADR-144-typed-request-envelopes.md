# ADR-144: Typed Orleans request envelopes

Status: Accepted by owner direction 2026-10-23. In source; Linux qualification pending.

## Decision

Internal requests between KeyLoad grains are `GrainRequestEnvelope` records with
explicit `[GenerateSerializer]`, `[Id]` and `[Alias]` contracts. `GrainRequestCodec`
issues an envelope with its purpose, request identity, database incarnation,
principal, read or command kind, stable command identity, typed operation value and
expiry, and every receiving grain decodes it through the same codec, which validates
that scope against the current incarnation and clock before any effect.

The operation travels as its own generated `[GenerateSerializer]` contract in the
envelope's `Payload` field. Orleans serializes it once with the envelope; no hop
encodes the operation into a private byte buffer and decodes it again. The codec
checks the operation's contract at issuance (public graph and query reads, and all
commands, keep public null elements for owner validation; other reads are strict),
and every receiving hop checks it again before effects. Readers cast to the exact
expected type. Database commands keep the same typed input in
`DatabaseCommand.Input`, so Core reads it without decoding; one kind-to-type table
(`DatabaseCommandInputTypes`) admits exactly the declared input type for each
command kind.

The envelope is not signed. The earlier design serialized it into a `KLT2.`
string token with an HMAC over the bytes and verified that MAC on every hop. Every
hop already ran inside Orleans between silos of one cluster; the string token added
Base64 and MAC work, a second parser with its own character bounds, and a signing
key in the request path without adding a trust boundary.

## Trust model

- The public API (HTTP, SDK, MCP, Postgres wire) authenticates the caller and then
  issues the envelope server-side. Clients never construct, see or replay one.
- Orleans transport between silos is the trust boundary for the envelope itself.
  ManagedCode.Orleans.RapidMembership admits silos with the cluster secret.
- Authority never travels in the envelope. Each grain reloads the persisted
  principal and policy epoch immediately before execution and rejects an envelope
  whose purpose, incarnation, expiry, request identity, command identity or payload
  shape does not match its own expectations.
- Connection close uses the typed `GrainConnectionCloseControl` record, validated
  against the owning database incarnation, connection identity and expiry.

## What stays signed

Database claims that leave the cluster or outlive a request keep their HMAC through
`CoreClaims` with the store identity key: query and change-feed cursors, queue
leases and receipts, transfer intents and witnesses, backup resume tokens. The owning
database signs and verifies them. A background coordinator that only needs an
identity from a witness it received from that owner over Orleans reads the framed
claims without the MAC; the owner verifies the signature again when the witness is
applied.

## Consequences

- `IConnectionGrain`, `IRequestExecutionGrain`, `IPartitionQueryGrain` and the
  background worker interfaces carry `GrainRequestEnvelope` parameters. Physical
  storage services expose typed copy and maintenance operations; request execution
  borrows the local physical owner through DI.
- The string token bounds (`GrainRequestTokenBounds`, the connection-close token
  character limit) and the `KLT2.` prefix for grain requests are removed.
- Scope tests mutate the typed envelope directly instead of re-signing a token;
  tampering tests about MACs no longer apply to grain requests.
- The former encoded payload field IDs are retired: envelope field 9 and
  `DatabaseCommand` field 1 are never reused; the typed members use new IDs.
- MCP and SQL issuance pass typed operations. SQL compiles CREATE/DROP TABLE,
  SELECT and INSERT straight to the typed operation without a JSON round trip.
- The ceiling on the encoded size of the decoded MCP operation existed only for
  the retired encoded payload and is retired with it. Public admission stays
  bounded by the HTTP body and MCP frame limits, the SQL request bounds and the
  Orleans message size limit; the accepted input set is not claimed to be
  byte-for-byte identical to the former one.
