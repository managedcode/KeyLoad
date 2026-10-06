# StorageRecovery: first-release current format

Status: accepted current-product contract, 2026-10-06; implementation and
qualification pending. This is KL-043's active scope under
[ADR-116](../../ADR/ADR-116-first-release-current-format.md). The root owner
super rule governs separately requested migration or legacy work. Current
qualification requires real operations against the current source and format.

## Contract and ownership

Keep one current generated Orleans persistence contract over node-local ZoneTree.
Current identity epoch7, WAL4 and checkpoint5 identifiers remain exact; this
cleanup does not renumber bytes, aliases, field IDs, digests or signing purposes.
Ordinary open, replay, snapshot install and backup/restore reject unsupported or
corrupt formats before publishing usable state. Keep replication and atomic
journals, physical owner locks, ordered apply, scoped read cuts and RF3 barriers.
No old-format reader, automatic conversion or runtime fallback is supported.
New stores are born with the current required runtime-journal reader contract;
there is no unmarked-store promotion, automatic backup/marking or dual identity
magic reader. Preserve current contract value1 and serialized field IDs; missing,
zero or unknown required capabilities fail strict admission. Current outcome-v2
records and locators retain their identity and integrity checks; outcome-v1 keys,
locators and missing-field compatibility fallbacks are removed. Unknown scope
for a current rejected operation remains an actual current outcome, not legacy.

The current reader is capability1 with identity magic `0x364449444C4B`.
Create fresh stores with capability1 explicitly; a deserialized absent field must
remain an unsupported capability0, never inherit a permissive constructor default.
Keep native StoreIdentity Id8 and peer discovery Id7 unchanged. All production
open/write/backup/snapshot paths require the current identity. Remove the reader
publication method, automatic adoption backups and per-record legacy checks;
startup validates both already-current physical stores without modifying either.
Fresh/current reopen, snapshot/backup/restore and real runtime-journal operations
must prove capability and exact state; corrupted/missing/zero/unknown capability
or magic must reject before file mutation. The configured discovery evidence is
computed from both actual current stores, not supplied by a caller. Use only the
current profile and centralized configuration sections, without obsolete aliases
or conversion commands.

RF3 preparation builds and verifies only the current server image. Its receipt
binds the actual source, image digest and three Aspire-owned resources; it has no
historical-image prerequisite. The request probe captures bounded current phase
records only. Remove prior-server image producers and mixed-image discovery
capture, preserving real discovery admission, incompatible-capability fencing,
current physical-shard mismatch recovery, signed stale-purpose rejection,
authorization, cancellation and joined shutdown. The current-image control must
perform real SDK/MCP operations and verify state; rejected operations must leave
state unchanged. CI retains the current build and every current required suite.

Current-format restore remains a real product operation with validated artifacts,
new incarnation and paused dispatch. Current-format crash recovery remains
mandatory. SQL/client interoperability, Orleans activation migration and native
protocol rejection are distinct active requirements; this correction does not
remove them.

## Requirements, acceptance and tests

| Requirement | Measurable acceptance and verification |
|---|---|
| REQ-STORAGE-007: qualify the first-release format | AC-NATIVE-001: real current writes, close/reopen, WAL replay, compaction and checkpoint install preserve exact records, identity authority and applied cut. Retain `RecoveryTests`, `FrameBudgetTests`, `PreparedTransactionTests`, current snapshot and replica recovery cases. |
| REQ-STORAGE-021: strict format admission | AC-NATIVE-002: real native identity/WAL/checkpoint operations reject unknown versions, invalid checksums and complete corrupt frames with typed safe errors; original files and state remain unchanged. Retain or port the current-format rejection flows from existing storage cases without generating an old database. |
| REQ-STORAGE-024: incompatible peers are fenced | AC-NATIVE-003: valid current signed SDK/MCP operations succeed through actual Aspire RF3; incompatible signed purposes/capabilities cannot dispatch or alter committed state. Retain peer/request-purpose and real current RF3 caller controls. |
| REQ-NATIVE-004: no migration or legacy execution | AC-NATIVE-004: the actual AppHost recovery model starts its current runner without historical probe resources, old-source archives, old-server image preparation or prior-format environment/settings; profile loading accepts only its current contract and has no conversion command; native current outcomes use only outcome-v2 and current locators with no v1 fallback. Normal/scalar runners execute retained whole current flows. Static CI governance inventories verify removed paths, separately from functional proof. |
| REQ-NATIVE-005: current recovery resources settle | AC-NATIVE-005: real process kills and restart preserve one complete acknowledged cut; all owned processes/readers/resources settle and failures remain visible. Existing process-recovery, source-manifest settlement and RF3 shutdown cases remain mandatory. |
| REQ-NATIVE-006: cleanup does not remove active safety | AC-NATIVE-006: complete enabled build, formatter, analyzers and relevant normal/scalar/recovery/RF3 suites pass; native functional coverage retains current production contributors and excludes removed code and obsolete migration tests. No manufactured coverage or acceptance closure. |
| REQ-NATIVE-007: admit only the current physical node layout | AC-NATIVE-007: actual owner startup rejects unknown root entries or links before initial root/lock/store mutation; complete original state and permissions remain unchanged. Known current partial recovery layouts remain admissible, owner exclusion is retained, and removal of only the test-owned invalid input permits a healthy current write/reopen. Current process and RF3 qualification remains required. |

## Ordered implementation and integration

1. Root records the owner correction, requirements and ADR, and reviews exact
   consumers before releasing disjoint source scopes.
2. Production worker removes only old-format converters/readers/coordinators
   under StorageRecovery in Storage.ZoneTree, Replication and Server. Keep a
   safety primitive with an actual current caller; remove it if exclusively legacy.
3. AppHost/test worker removes historical preparation and migration process/image
   cases plus their exclusive helpers in AppHost, scripts, RecoveryTests,
   IntegrationTests and CrashHost. Shared current RF3/profile helpers remain
   until every real current consumer is preserved.
4. Unit/CLI worker removes obsolete unit migration/probe cases and exclusive
   fixtures, preserving current corruption and authorization-order operations.
   Root owns shared entry points, current-only reader/outcome/profile and prior
   KeyLoad protocol seams, public contracts, workflows,
   source/coverage inventories, policy/docs and registry joins.
5. Root reviews all guarded diffs, runs the canonical full build/format/governance,
   then actual Aspire normal/scalar/recovery/RF3 operations and Linux delivery
   gates. Source-only cleanup closes no product task.

No live user directory, persisted data or unrelated chat work is deleted.
Rollback is a source checkpoint, never a format downgrade or an old binary over
current data. A future released-format upgrade needs separate owner direction.
Frontend and new public SDK/MCP schemas are N/A: no new product operation or
dependency is introduced. Existing callers continue using current-format data.

```mermaid
flowchart LR
  Caller[SDK SQL MCP] --> Request[Authorized Orleans request]
  Request --> RF3[Three current voters owned by Aspire]
  RF3 --> Store[Node local ZoneTree current format]
  Store --> Recover[Current WAL checkpoint recovery]
  Unsupported[Unknown or corrupt format] --> Reject[Fail before publication]
```


## Current ancillary execution and owner validation

TASK-SR-CURRENT-ANCILLARY-031 removes the five exclusive prior-image scripts and
CI preparation/environment references. Recovery's actual AppHost model contains
only its native recovery runner; it has no historical-image prerequisites. Port
that infrastructure model check without replacing real process-recovery evidence.
Current shared recovery cleanup and file-inventory helpers are renamed by their
actual responsibility. Remove only the uncalled old node-settlement branch;
retain actual child exit/readers/disposal, failure aggregation, owned-root cleanup,
file digests and handle-release proofs in their current callers.

TASK-SR-CURRENT-OWNER-032 makes the existing replica membership guard mandatory
with existing hard state for every ordinary and benchmark open. Fresh stores write
both records in one atomic commit. Missing/orphan/malformed/mismatched metadata
fails before publication without adding a guard to an existing store. The guard's
current key, native alias and fields stay unchanged; decode is bounded by the
larger of the configured native collection budget and configured voter count,
so legitimate current odd production groups remain supported. Ordered voter and
incarnation validation, snapshot ownership and RF3 remain mandatory. Whole real
store create/reopen, term/vote, missing-guard rejection/no mutation and healthy
follow-up replace the exclusive marker-free production case.

The same owner task removes NodeOptions' unused discovery-timeout alias. Its peer
factory accepts only the canonical validated PeerDiscoveryOptions.ConnectTimeout.
The central pre-physical-ownership Peer.Value resolution preserves endpoint/HMAC/
replay validation and effective connect duration strictly below the replica RPC
deadline. Preserve the actual RPC/election ordering and all signing authority.
Actual configuration/owner admission flows test valid configured policy and
invalid pre-file rejection; getters are not acceptance proof. Root freezes exact
ownership, reviews guarded private packets, joins consumers and runs enabled
build/format plus Aspire whole-operation, recovery and RF3 gates.

## Current WAL admission without obsolete format inventories

TASK-SR-CURRENT-WAL-036 implements REQ-STORAGE-021 / AC-NATIVE-002 in
Storage.ZoneTree's current journal preflight, recovery and backup validation.
Only the current WAL and checkpoint signatures are accepted. An unsupported
complete signature or identity version fails FormatUnsupported before provider
opening, truncation, restore publication or canonical effects. Invalid current
frame length/order/checksum remains Corruption; incomplete current tails retain
the existing verified-prefix recovery contract. Remove obsolete signature
constants and specialized old-format recognition branches, with no alternate
reader or converter. Current backups retain their exact cut, byte limits,
checksum/manifest proof and all unchanged-source/destination rejection oracles.

The private packet owns ZoneTreePersistenceFormat.cs, ZoneTreeJournalPreflight.cs,
ZoneTreeJournalRecovery.cs, ZoneTreeBackupJournalValidation.cs and the current
NativeBackupCutTests.cs unsupported-header flow. Root joins only after full
semantic review and all base/post guards, then runs enabled canonical build,
formatting and actual Aspire current storage/backup/recovery tests. Any additional
consumer is reported before ownership expands. Source inspection closes no AC.

### Current native test inputs and operation boundaries

TASK-SR-CURRENT-FIXTURES-041 maps REQ-STORAGE-021 / AC-NATIVE-002 and the
current BackupRestore roster requirements. Remove exclusive prior-record bytes,
prior codec branches and JSON metadata representations. Keep current native
unknown-signature/version, length/order/checksum, exact-cut and whole restore
rejection flows with no publication, unchanged input and healthy follow-up.
Unknown complete signatures are FormatUnsupported. Corruption controls must
retain the accepted current signature and damage its actual fields or checksum;
identity checksum controls retain the independently frozen current identity magic.
Guarded unknown-identity fixtures use an arbitrary unsupported future version,
not an earlier product representation, and retain real child/lock/pipe settlement.

Cross-partition reverse-edge/empty-placement roster tests must seed documents
through actual authorized current Batch operations. The destination already has
its atomically registered first-write row; delivering and completing the reverse
edge, deleting the source edge and reopening must preserve the destination row
and first-seen identity. Do not insert an unregistered document as a prior-roster
positive input or promise a census/backfill. Preserve actual receiver state and
all cross-destination-effect and empty-placement assertions.

The current corrupt-snapshot no-mutation oracle begins after the ordinary current
store open/recovery has joined and ends before its normal provider disposal.
Compare the complete retained native file inventory around the rejected install
within that owner window, together with unchanged identity/cut/data and a healthy
following install/backup/restore. Native open/close housekeeping must not be
misattributed to the rejected operation or hidden by excluding authoritative files.

TASK-CURRENT-SNAPSHOT-NATIVE-FILE-067 implements this AC-NATIVE-002/006 oracle
with an internal read-only observation handle in `KeyLoad.Storage.IO`. The test
controls the live owner and performs no concurrent writes during each inventory.
Native no-follow/nonblocking open and retained descriptor/path identity checks
must reject non-regular or replaced inputs. Use the validated storage execution
buffer setting; read every file's actual complete bytes without acquiring or
releasing the owner's advisory lock. Preserve the competing-owner lock assertion
and the original full inventory equality; do not skip locked WALs or substitute
empty bytes. This is deterministic test observation, not a coherent snapshot
against concurrent writers or a production ownership bypass. Production opens
retain their existing lock contract. Root owns the API/platform join and verifies
the complete current maintenance/rejection/install/backup/restore/reopen operation
through the Aspire-owned runner after full build and formatting.

## Current node-root admission

TASK-SR-CURRENT-LAYOUT-044 implements REQ/AC-NATIVE-007 under ADR-116. Replace
specific obsolete filenames with one strict current root inventory: optional
regular `node.owner.lock` and optional non-link directories `database`, `replica`,
`snapshots`, `search-indexes` and `backups`. Their internal contents remain owned
by their existing native owners; admit valid partial prefixes after interruption.
Do not add unused names, infer authority from directory presence or recursively
replace native snapshot/index/store validation with a server filename list.

Inspect an existing root and entries without create, chmod, delete or provider
open. Unknown entries, wrong entry kinds and links reject FormatUnsupported with
a static safe detail. An absent root may be created only for fresh ownership.
After exclusive node ownership, inspect the root again before opening either
store; retain the existing first-failure/cleanup aggregation. Cooperating writers
use the same exclusive lock. Arbitrary concurrent external filesystem replacement
is not qualified by enumeration; do not advertise such protection. A race found
on the second check must reject and settle the acquired owner without deleting
foreign state or claiming that a newly acquired lock never existed.

The private worker owns only PartitionStores, a cohesive StorageRecovery root
validation helper and new real PartitionHost admission cases/fixtures. Root owns
this contract, the ADR, exact path guards, review/join and serialized native gates.
Whole flows retain sentinel bytes/modes and absence of first-check side effects,
then remove only their owned invalid input and prove current write/reopen effects.
Existing snapshot recovery, index restart, backup/restore and owner-lock cases
remain mandatory controls. SDK/MCP and frontend schemas are N/A; this is current
physical owner admission, not a new public operation or format conversion.

## Actual omitted current native capability inputs

TASK-SR-CURRENT-MISSING-043 extends REQ-STORAGE-021/024 and AC-NATIVE-002/003.
An explicit zero property or JSON omission does not prove omitted native fields.
Use the actual current generated Orleans payload, verify the required field was
physically omitted by a bounded current-wire test input, and retain the current
envelope magic, identity fields and recalculated valid outer checksum. No prior
type, binary, historical store or alternate runtime serializer is allowed.
Actual store open must reject before journal/tree effects, release owned handles,
preserve the complete original inventory and permit a valid current restore and
healthy write/reopen afterward. The generated codec's actual error remains an
unmeasured gate until this operation executes through AppHost.

For authenticated peer discovery, every cohort admission path must require the
current reader capability, including existing-catalog startup and ordinary
requests. Root review of actual native sources precedes a guarded implementation.
Real signed discovery/request/no-effects and recovered healthy SDK/MCP flows are
required; direct record construction, a fake peer or a coordinator assertion does
not qualify omitted-wire RF3 behavior. The paired-store preflight/no-mutation
contract also remains open until current native operations prove it.


## Current-reader cohort admission repair

TASK-NATIVE-CURRENT-PEER-049 maps REQ-STORAGE-024 / AC-NATIVE-003 under
ADR-116. One observation predicate requires current protocol plus reader
capability1; protocol compatibility remains a separate recorded fact. Ordinary
cohort admission, direct voter resolution and cached readiness count only voters
with that current contract and ready transport. Any observed signed non-current
capability is OwnershipLost/IncompatibleCohort, including a not-ready voter;
a current-contract but unavailable/not-ready voter retains its existing unavailable
classification. Keep configured-voter iteration, majority threshold, cache expiry,
original signing, cancellation and joined shutdown. Missing capability remains0.

Fresh native catalog bootstrap still requires every configured current voter.
An existing catalog must pass current-contract majority admission before
RuntimeJournalAdmission.Open; it does not acquire a new all-voters availability
requirement. Preserve the existing authorized catalog read, unique request grain,
startup deadline, clock and fresh-bootstrap wait. Unsupported local contract fails
strictly; only current-contract transport unavailability may remain pending.
Physical capability evidence continues to come from both validated node-local
stores, with no permissive constructor default or advertised authority override.

The worker owns only the root-guarded discovery/admission/startup paths, the
static unsupported-replica detail, supplementary signed-socket whole-operation
cases and actual RF3 positive discovery assertions. Unit endpoint controls remain
policy operations, never physical RF3 evidence; healthy current test inputs state
capability explicitly. Real SDK/MCP requests retain before-dispatch rejection,
no effects and healthy follow-up acceptance. A physically omitted-field RF3
negative requires a separate bounded server-owned native fault contract and is
still open; no fake peer, re-signing bypass or compatibility hook is admitted.
Root owns review, serial source joins, native gates and exact-source Linux proof.
