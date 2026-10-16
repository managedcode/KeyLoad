# Native functional development topology

The functional fixture selects `--KeyLoadTests:FunctionalNodeCount=1` or `=3` in its own AppHost builder, together with explicit `KeyLoad:Ephemeral=true` and an absolute fixture-owned `KeyLoad:DataRoot`. This is not a native test-runner flag. Ordinary composition still creates three voters. Benchmark selection, two-group profiles and private fault/probe profiles cannot be combined with this selection.

The existing AppHost resource loop creates exactly node1 for the first gate, or node1/node2/node3 for the second. Each physical server retains its own ZoneTree roots, native Orleans silo, Azure Tables emulator-backed Orleans membership, discovery, distributed grain directory, HTTP readiness, signed peer identities and connection-owned public request execution. No hidden silos, replacement membership or independently started server resources are introduced. The original AppHost/fixture owns all resource startup, readiness, original client/process shutdown and retained-root decisions.

Only the one-node profile receives `KeyLoad__DevelopmentSingleVoter=true`. It uses the original replica consensus, commit, apply, journal and receipt paths with one configured voter and majority one. This provides no replica failure tolerance and does not qualify RF3 durability. The existing generic `QuorumProcessDurable` receipt name refers to the configured quorum; it is not a claim that a second or third replica exists. The functional three-node gate uses the ordinary RF3 majority of two, without the development or benchmark flag. Each gate uses its own fresh physical profile; the persisted original voter-list/incarnation fence rejects reopening a different topology over the same authority.

Local development image preparation admits only the two exact native selectors:

- `/*/*/FunctionalDatabaseGateTests/SingleInstanceRunsEveryModelThroughSdkMcpAndSqlWithPersistedUsers`
- `/*/*/FunctionalDatabaseGateTests/ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql`

The native runner executes the first gate alone, then the second only after the first passes. A class wildcard is not enrolled. Image provenance/receipt validation and conflicting GitHub/local image guards remain in force. No image build, test run or acceptance pass follows from this source packet.

`FunctionalSingleVoterTopologyTests.ExplicitDevelopmentSingleVoterPreservesDefaultRf3AndRejectsConflictingTopology` checks the actual NodeOptions and ReplicaConfiguration contracts, both acknowledgement majorities, and refusal of implicit single voters, mixed benchmark/development flags and a development flag on three voters. The genuine Aspire SDK/MCP/SQL operation scenarios remain the separate runtime qualification, followed by all original foundation and Linux release gates.

## Physical file cases, owner direction 2026-10-16

The functional qualification includes two distinct requested layouts. The shared
layout means the same physical directory and the same ZoneTree files in separate
silo processes. Giving each silo a child directory on one disk does not prove
that layout.

| Case | Required evidence | Current boundary |
|---|---|---|
| Separate files for each silo | Actual Aspire mounts, distinct persisted store identities and physical file identities; genuine SDK/MCP/SQL model operations, original receipt replay and two joined cold reopens over the retained roots. | Existing supported RF3 topology; strengthened physical-file runtime qualification remains pending. |
| All silos read the same live ZoneTree files | Actual same-file identity across the processes plus a supported coherent open/read/write, recovery and ownership contract, followed by the complete public operation flow. | Positive qualification is OPEN. The pinned engine and current owner do not provide this mode. |

ZoneTree 2.0.1 is bound to source commit
`ec774af2223bd87457a0aa985908f0b4e508e4c8`. Its ordinary
[loader](https://github.com/ZoneTree/ZoneTree/blob/ec774af2223bd87457a0aa985908f0b4e508e4c8/src/ZoneTree/Core/ZoneTreeLoader.cs#L132-L190)
opens writable metadata/recovery state; it exposes no qualified whole-tree
read-only open for concurrent independent processes. The selected native
[sync WAL](https://github.com/ZoneTree/ZoneTree/blob/ec774af2223bd87457a0aa985908f0b4e508e4c8/src/ZoneTree/WAL/Sync/SyncFileSystemWriteAheadLog.cs#L55-L62)
uses `FileAccess.ReadWrite` with `FileShare.None`. Individual segment read sharing
does not establish a coherent cross-process database contract. KeyLoad acquires
its exclusive `node.owner.lock` before opening identity, either store or recovery.

A separate whole-operation ownership guard exercises real competing server
containers against the same live owner directory. Aspire owns their explicit
startup and shutdown. A bounded original `IOException` identifying
`node.owner.lock`, the native storage-open stack, nonzero process completion and
absence of readiness must prove the refusal; an arbitrary startup error or timeout
does not qualify it. The original three voters must retain their exact data and
receipts, accept healthy SDK/MCP/SQL continuation, and complete the same two cold
reopens. These contenders are diagnostic resources, not additional RF3 voters.

That expected refusal is protection evidence only. It does not satisfy the
requested successful shared-files case, permit weaker locks, establish independent
replica copies, or authorize a replacement engine, compatibility path or format
migration. Keep the positive case visibly OPEN until its actual storage contract
and complete runtime evidence exist.

REQ-FILES-001 requires physical independence of the supported RF3 store files.
REQ-FILES-002 retains the requested literal same-file shared-directory mode as an
open capability, rather than substituting child directories. AC-FILES-001 maps
physical identities, complete public operations and both retained-root cold cuts
to `FunctionalFileOwnershipRf3Tests.DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens`.
AC-FILES-002 requires all competing diagnostic processes and readers to settle
with the exact native exclusive-owner refusal before healthy-owner release.
AC-FILES-003 requires a supported coherent same-file engine contract and actual
positive public-operation evidence; no current test result closes that criterion.

The exact local native selector for AC-FILES-001/002 is
`/*/*/FunctionalFileOwnershipRf3Tests/DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens`.
The focused `functional-gates` GitHub dispatch enrolls the same complete case
after the existing cold RF3 gate and requires its original one-case passing TRX
and receipt. Source enrollment is complete; actual execution remains pending.
