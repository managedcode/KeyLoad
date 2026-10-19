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
  warnings-as-errors solution build passed. Owner approved exactly 10.1.5;
  commit `06154822dec61b586d8549fd3633dd618b25ff55` is pushed and its
  [Release workflow](https://github.com/managedcode/Storage/actions/runs/39242642161)
  completed successfully, including Browser Stress, production coverage, NuGet
  publication and every-package source-commit verification. Server and FileSystem
  10.1.5 public nupkg source commits were independently verified; KeyLoad central
  references are updated once to those published versions. Consumer restore and the complete Debug solution build with warnings as errors
  pass (0 warnings / 0 errors). Focused consumer checks are recorded below.
- Native controller/provider composition, metadata-only publication and fresh
  scope/row authorization are implemented. Server Debug build passes with zero
  warnings/errors. Nine file tests pass for metadata publication, quota/row authorization, native
  MVC route discovery and physical identity isolation. Actual Aspire HTTP
  acceptance is blocked by the independently reproduced text receipt startup
  failure recorded below.

## Current KeyLoad checks

Public-feed restore, the complete Debug solution build with warnings as errors,
repository governance and scoped format/format verification pass. A subsequent
UnitTests project build also passes with the source-identity gate enabled.

Focused TUnit checks on the final assembly:

- KeyLoadClientNullReadTests: 2 pass, including rejection and healthy continuation
  after all seven remaining nullable-read routes.
- McpOutputSchemaTests: 5 pass.
- ResourcePolicyUpdateRejectionTests: 6 pass.
- ContractFamilyTests: 13 pass.
- ResourcePairBindingWholeFlowTests: all 4 fail at complete DocumentResult equality
  in the initial healthy mixed-model step, before resource-pair fault injection.
  Assertions are retained; this is an open gate, not a passing whole-operation claim.

The new routes are composed in source. These focused checks do not establish
complete file API acceptance: physical recovery, cancellation/endurance, payload
backup consistency and deployed shared-provider cluster qualification remain open.

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

The actual Aspire HTTP scenario is present and runs the native AppHost with
Azurite. It fails before any file operation: the node cannot open the fresh text
projection receipt. The same failure is independently reproduced by existing
`TextOnlineWholeOperationTests.OrderedPublicationPreservesFullOriginalReceiptAndSameViewBilingualReads`:
`TextEnvelopeCodec.Decode` -> `TextRootFiles.VerifyReceipt` reports
`The execution text projection is corrupt.` No assertion or startup protection
is weakened. HTTP upload/download, ranges and revocation are therefore unqualified
until this startup blocker is resolved. This checkpoint does not close physical
restore, cancellation/endurance, shared-provider multi-silo or Linux delivery gates.
