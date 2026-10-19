# ManagedCode.Storage file API implementation

Owner direction: 2026-10-19. Public files use ManagedCode.Storage contracts and
server implementations. ZoneTree stores authorized metadata and physical object
references; it does not store file payloads or binary upload parts.

## Work and completion criteria

1. Inspect Storage contracts, server controllers, providers and current
   KeyLoad command/read/restore ownership. Update ADR-038 and BlobStorage.
2. Replace the binary-part engine with metadata-only file publication, reads and
   deletion through the existing authorized Orleans request path.
3. Compose the existing Storage server/provider implementation directly.
   Add only KeyLoad metadata and authorization integration. Do not introduce an
   IStorage adapter, custom transfer stream or multipart engine. Remove the
   superseded HTTP, SDK, MCP and SQL chunk contracts and their exclusive support.
4. Cover actual provider writes, metadata publication, reopen, unauthorized
   access, cancellation, bounded reads and cleanup with real implementations.
5. Run relevant tests, formatting, warning-as-error build and governance.
   Record focused local checks separately from distributed/Linux qualification.

## Invariants and risks

- Fresh persisted scope and row authorization remains mandatory before effects.
- No request containing file bytes enters the persisted database command path.
- Physical objects have server-created immutable identities. Metadata publication
  determines visibility. An ambiguous publication outcome must not delete data.
- File persistence, metadata replication and backup of physical objects are
  distinct contracts. Metadata copies alone do not acknowledge payload copies.
- Physical reclamation must not delete a published object or a borrowed stream.
- Existing unrelated checkout changes stay visible and outside this checkpoint.

## Verification status

Implementation and runtime checks are pending. Existing raw-part BlobStorage
evidence does not qualify the replacement.

## Progress

- Canonical feature, ADR-038 and architecture ownership updated.
- Old BlobStorage runtime/transport/restore code and serializers removed.
- Rejected consumer adapter/stream implementation removed.
- Storage dependency: five original-failure cases reproduced; all nine focused
  tests pass after the combined owner repair. Full checks and published
  delivery remain open.
- KeyLoad module composition, metadata integration and acceptance tests
  are incomplete. Removal is not a file-API readiness claim.
