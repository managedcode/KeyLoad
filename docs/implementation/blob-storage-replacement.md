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

Module integration is implemented in source; end-to-end acceptance is under verification. Existing
raw-part BlobStorage evidence does not qualify the replacement.

## Progress

- Canonical feature, ADR-038 and architecture ownership updated.
- Old BlobStorage runtime/transport/restore code and serializers removed.
- Rejected consumer adapter/stream implementation removed.
- Storage dependency: five original-failure cases reproduced; all nine focused
  tests and all 790 non-stress cases pass. Final coverage is 85.41% line / 72.16%
  branch across all 19 packages. Restore, format, format verification and a
  warnings-as-errors solution build passed for the original 10.1.5 repair;
  commit `06154822dec61b586d8549fd3633dd618b25ff55` is pushed and its
  [Release workflow](https://github.com/managedcode/Storage/actions/runs/39242642161)
  completed successfully, including Browser Stress, production coverage, NuGet
  publication and every-package source-commit verification. Server and FileSystem
  10.1.5 public nupkg source commits were independently verified. This is the
  original dependency release evidence. Current consumer package versions are
  pinned in `Directory.Packages.props`; the approved package refresh selects
  published Server and FileSystem 10.1.6.
- Native controller/provider composition, metadata-only publication and fresh
  scope/row authorization are implemented. Server Debug build passes with zero
  warnings/errors. Nine file tests pass for metadata publication, quota/row authorization, native
  MVC route discovery and physical identity isolation. The current local
  three-node whole-model test exercises native HTTP uploads, exact downloads
  and denied writes through the real Aspire composition.

## Current KeyLoad checks

The approved package refresh restores from the public feed and builds the complete
Debug solution with warnings as errors. The local
`ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql` case passes with
three persisted copies, including native file uploads, exact-byte downloads and
rejected unauthorized writes. Focused storage, text and CQRS checks also pass.

These checks do not establish complete file API acceptance. Physical recovery,
cancellation/endurance, payload backup consistency and deployed shared-provider
cluster qualification remain separate open gates. Linux checks must qualify the
exact committed source and its selected packages.

## Connected metadata and authorization checkpoint

The native Storage server/controller and filesystem provider are composed in the
server. Metadata-only reservation, staging, revision publication, row authorization,
quota accounting, listing, retirement and protected cleanup use the existing
signed connection-grain path. All twelve MVC actions have capability metadata;
no raw metadata mutation is admitted through the generic public gateway.
The admin file browser uses the new native metadata route and encrypted cursor.

Current local evidence:

- `dotnet restore KeyLoad.slnx`: pass.
- Scoped `dotnet format` and complete solution `--verify-no-changes`: pass.
- Complete Debug `dotnet build KeyLoad.slnx --no-restore -warnaserror -m:1`:
  pass, zero warnings/errors.
- All nine BlobStorage unit cases: pass. Seven use real ZoneTree metadata operations; the other two verify MVC route discovery and physical
  namespace/session isolation. Scope includes fresh row-policy changes,
  revocation, quotas, concurrent publication and protected cleanup.
- PartitionRecordInventoryTests: 1 pass; 69 current partition families.
- ContractFamilyTests: 13 pass.
- FileStartupDiagnosticTests: 1 pass; original startup causes survive a long
  shutdown stack within the existing diagnostic limits and secret redaction.
- Repository governance and changed dashboard JavaScript syntax: pass.

The earlier text receipt startup failure no longer blocks the local whole-model
Aspire scenario. Its successful native file operations establish that scenario's
scope only; range, cancellation, physical restore and deployed shared-provider
acceptance require their own original execution evidence.
