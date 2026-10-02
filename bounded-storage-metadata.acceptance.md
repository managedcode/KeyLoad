# Bounded storage metadata acceptance

Chosen brainstorm: bounded-storage-metadata.brainstorm.md. Lead accepts the
following contract before source writes; ADR048 governs compatibility/boundaries.
Related REQ-BACKUP-005, REQ-STORAGE-011, AC-MP-006/012 and AC-CQ-008.

Actors/entry points: offline operator uses ZoneTreeStore.CreateBackup/Restore;
node-local store initialization uses the existing ZoneTreeStore constructor.
Trusted persisted credentials/roles and physical lock/gate ownership stay exact.
No UI, public API signature, wire/schema/version, cluster placement, journal cap,
checkpoint, archive format, dependency or online restore change is in scope.

Assumption: offline backup files are caller-controlled; concurrent mutation of
the whole backup is not a promised consistent-cut operation. The metadata reader
must nevertheless never allocate/read an unbounded region when a file grows.

| Criterion | Pass / fail and automated or explicit review evidence |
|---|---|
| AC-BSM-001 | Manifest bytes are limited inclusively to16384 and identity-envelope bytes to4096. An opened FileStream length precheck plus authoritative at-most-limit+1 read bounds allocation and detects oversize; post-read length also cannot exceed limit. No File.ReadAllBytes, unbounded MemoryStream or returned copy remains on these production paths. Real-file exact-limit whitespace succeeds; one-over fails FormatUnsupported with the existing file-specific safe detail. Same positive/oversize checks exercise constructor and restore where applicable. Static complete-reader review proves growth boundedness; a timing-dependent concurrent writer test is an explicit exception because it cannot deterministically select this branch without a forbidden fake/seam. |
| AC-BSM-002 | Restore verifies identity manifest length/SHA over the same owned bytes later used for envelope parsing, inner checksum and payload parsing, with no second identity-file read. All outer files are verified before identity parsing; bounded malformed identity plus corrupt WAL still returns existing outer verification Corruption first. Valid backup restores data, new incarnation/signing identity and paused dispatch. Existing schema/write bytes and durable barriers stay exact. Tests use real backups and independent SHA/file edits; same-region/single-read guarantee additionally requires full source dataflow review, not a claimed measured physical-I/O counter. |
| AC-BSM-003 | Nonempty destination Conflict stays first and preserves contents, including when metadata is oversized. Oversized metadata, unsupported manifest/version, missing files, outer hash/length mismatch, inner checksum mismatch, malformed/null/depth/unknown-member JSON fail closed at existing appropriate boundaries; metadata rejection before destination creation leaves no usable destination. Existing bounded-input exception types/details and verification order remain exact. Real-file negative tests cover these paths; existing valid identity/version/signing-scope tests remain retained. Startup oversize failure releases owner lock: replace with original identity and reopen the same real store successfully. No fake, catch-all or suppression qualifies this criterion. |
| AC-BSM-004 | After warming the same real public rejected Restore path, restoring a prepared8MiB metadata file allocates less than half that file's size on the calling managed thread; fixture preparation/output/assertion allocations stay outside the measured synchronous call. Test manifest and identity oversize independently, updating real manifest length/hash for the latter. No throughput/RSS/native allocation/power-loss claim is inferred. Actual GitHub raw test evidence is required before any resource-improvement claim. |
| AC-BSM-005 | Source is cohesive canonical StorageRecovery/BackupRestore with400/200/50/3 limits, strict central analyzers and exact Prostir EditorConfig preserved. Real storage/UnitTests/full builds, canonical format/governance and exact-SHA GitHub UnitTests/process-recovery/Docker Aspire RF3 .NET/MCP required gates pass. Existing unrelated source/work/artifacts remain intact; no local qualification. Source/build success alone fails final acceptance. |

Test strategy: new real-file Metadata* TUnit fixtures under
UnitTests/Features/BackupRestore cover BSM001..004 positive, exact/one-over,
malformed/security/checksum/precedence/cleanup and allocation flows. Existing
VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch and storage/frame/
recovery/RF3 suites remain broader regressions. Canonical invocation in GitHub:
dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore --configuration
Release, followed by existing RecoveryTests/IntegrationTests SDK/MCP jobs in ci.yml.
The runner is TUnit/Microsoft.Testing.Platform; no VSTest assumptions or collector
baseline are invented. Coverage80line/70branch and90critical remain mandatory,
but authentic configured collector/numeric baseline is separately unfinished.

Compatibility: normal current/earlier supported schema output is unchanged; files
above inclusive metadata budgets now fail explicitly, even if excessive whitespace
was formerly accepted. Rollout is one private reader/restore/test unit. Rollback
reverts that complete unit and its private constants; it does not alter persisted
files or retain an old reader/fallback/shim. ADR remains Accepted pending gates.
