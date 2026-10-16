# Native functional development topology

The functional fixture selects `--KeyLoadTests:FunctionalNodeCount=1` or `=3` in its own AppHost builder, together with explicit `KeyLoad:Ephemeral=true` and an absolute fixture-owned `KeyLoad:DataRoot`. This is not a native test-runner flag. Ordinary composition still creates three voters. Benchmark selection, two-group profiles and private fault/probe profiles cannot be combined with this selection.

The existing AppHost resource loop creates exactly node1 for the first gate, or node1/node2/node3 for the second. Each physical server retains its own ZoneTree roots, native Orleans silo, Azure Tables emulator-backed Orleans membership, discovery, distributed grain directory, HTTP readiness, signed peer identities and connection-owned public request execution. No hidden silos, replacement membership or independently started server resources are introduced. The original AppHost/fixture owns all resource startup, readiness, original client/process shutdown and retained-root decisions.

Only the one-node profile receives `KeyLoad__DevelopmentSingleVoter=true`. It uses the original replica consensus, commit, apply, journal and receipt paths with one configured voter and majority one. This provides no replica failure tolerance and does not qualify RF3 durability. The existing generic `QuorumProcessDurable` receipt name refers to the configured quorum; it is not a claim that a second or third replica exists. The functional three-node gate uses the ordinary RF3 majority of two, without the development or benchmark flag. Each gate uses its own fresh physical profile; the persisted original voter-list/incarnation fence rejects reopening a different topology over the same authority.

Local development image preparation admits only the two exact native selectors:

- `/*/*/FunctionalDatabaseGateTests/SingleInstanceRunsEveryModelThroughSdkMcpAndSqlWithPersistedUsers`
- `/*/*/FunctionalDatabaseGateTests/ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql`

The native runner executes the first gate alone, then the second only after the first passes. A class wildcard is not enrolled. Image provenance/receipt validation and conflicting GitHub/local image guards remain in force. No image build, test run or acceptance pass follows from this source packet.

`FunctionalSingleVoterTopologyTests.ExplicitDevelopmentSingleVoterPreservesDefaultRf3AndRejectsConflictingTopology` checks the actual NodeOptions and ReplicaConfiguration contracts, both acknowledgement majorities, and refusal of implicit single voters, mixed benchmark/development flags and a development flag on three voters. The genuine Aspire SDK/MCP/SQL operation scenarios remain the separate runtime qualification, followed by all original foundation and Linux release gates.
