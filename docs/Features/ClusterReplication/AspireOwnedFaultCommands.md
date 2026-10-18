# Aspire-owned abrupt faults for replicated-cluster tests

The existing Aspire `ContainerResource` owns every test lifecycle command under [ADR-034](../../ADR/ADR-034-cluster-comparisons.md), [ADR-117](../../ADR/ADR-117-native-tunit-ci-entry.md) and [ADR-136](../../ADR/ADR-136-native-orleans-partition-ownership.md). Native TUnit fixtures retain startup, readiness, SDK/MCP operations and joined shutdown. Parent specification: [ClusterReplication](../ClusterReplication.md).

`KeyLoadTests:ContainerFaults=true` explicitly selects the test fault command. Registration refuses non-ephemeral and comparison/benchmark resources. Each command is bound to the existing resource and its original `ContainerNameAnnotation`; the caller supplies only an expected full 64-hex container ID. The AppHost inspects its bound name, refuses a mismatching or non-running container, then issues `docker kill --signal KILL` against that exact inspected ID. Metadata inspection is read-only. No test caller launches a container-engine lifecycle command.

The per-reader 16,384-character output bound remains unchanged. Each process observes the validated application-cleanup timeout and joins all acquired tasks even when task acquisition fails. The stable Aspire 13.6.1 `WithCommand` callback is invoked through `ResourceCommandService`, without experimental process-command APIs or diagnostic suppression. The AppHost process owner bounds stdout/stderr and joins the process and readers on failure or cancellation. The typed result contains the actual engine exit code and output. Cancellation cannot prove that an unobserved SIGKILL did not take effect; the fixture shutdown remains responsible for every resource.

- REQ-CR-FAULT-001: only the explicitly selected ephemeral replicated-cluster test topology exposes abrupt fault commands; production and comparison resources expose none.
- REQ-CR-FAULT-002: malformed, stale and foreign container IDs are refused before any lifecycle effect.
- REQ-CR-FAULT-003: process failures, cancellation, reader joins and factual receipt fields are retained.

AC-CR-FAULT-001 maps to `AspireContainerFaultCommandTests`: rejected IDs leave running identities and committed content unchanged, followed by healthy operations and the kill/restart flow. AC-CR-FAULT-002 maps to `AggregateReplayClusterLeaderLossTests.AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay` through the real .NET SDK and official MCP clients (the test name reflects current source debt described in `sourceDebt` in [status.json](../../implementation/status.json)). Runtime qualification remains open until the adopted source runs through native Aspire. This is SIGKILL process-fault evidence, not power-loss durability.

```mermaid
flowchart LR
    T[Native TUnit fixture] --> C[ResourceCommandService]
    C --> R[Owned test ContainerResource command]
    R --> I[Inspect bound name and expected ID]
    I --> K[AppHost-owned SIGKILL process]
    K --> J[Join process and readers]
    J --> E[Receipt and native Start]
```

Source ownership: `build/KeyLoad.AppHost/ClusterReplication/Resources/ClusterResources.cs` (resource composition), `Commands/ContainerKillCommand.cs` (command registration), `Contracts/ContainerFaultCommandProtocol.cs` (typed invocation/result constants) and `Processes/ContainerFaultProcess.cs` (delegation to the AppHost process owner). In tests, `ClusterReplication/Scenarios/Resources/AspireContainerFaultCommand.cs` owns native invocation, `ContainerRuntime/ContainerRuntimeControl.cs` retains receipts and `ClusterFixtureComposition` enables the profile. These paths add no resource, launcher, topology, engine provider or package.

## Network isolation fault ownership

- REQ-CR-FAULT-004: mutating network-namespace isolation and restoration execute only through the same AppHost resource command service. The command is registered only by the diagnostic derived-image/NET_ADMIN test composition and requires the explicit ephemeral test fault flag. Ordinary product, comparison and ordinary replicated-cluster resources have no namespace command. Only unicast IPv4 peers are admitted.
- REQ-CR-FAULT-005: the caller supplies an expected full container ID and bounded typed rule fields, never executable paths or engine arguments. The AppHost derives the bound name, checks the admitted test resources, image, service user, fault labels, isolated network and sole CAP_NET_ADMIN capability, derives the peer addresses and builds only the fixed iptables operation on the Orleans silo port with operation-owned chains and bounded timeouts.
- REQ-CR-FAULT-006: intent is retained before invocation; actual exit code, stdout and stderr are preserved. An unobserved completion still requires AppHost namespace shutdown; cancellation is not proof of no effects. Read-only iptables listing/counters, `cat` and `sha256sum` evidence remain read-only callers.

AC-CR-FAULT-003: malformed rules and wrong, stale or foreign container IDs are refused with namespace rules and container identities unchanged, a healthy follow-up operation succeeds, and isolation and restoration run only through the AppHost command. The current consumer `DocumentSessionIsolationClusterTests` also asserts leader, term and session-token behavior that is source debt, so it does not qualify this criterion under the current cluster concept; qualification is open.

Source roles: `build/KeyLoad.AppHost/ClusterReplication/Resources/ReplicaIsolationResourceComposition.cs` registers the command after diagnostic admission; `Commands/ReplicaIsolationCommand.cs` owns native invocation and resource inspection; `Validation/ReplicaIsolationCommandRule.cs` owns closed typed rule validation; `Contracts/ReplicaIsolationCommandProtocol.cs` owns bounded fields. In tests, `Processes/ReplicaIsolationExecution.cs` invokes `Scenarios/Faults/AspireReplicaIsolationCommand.cs` through `ResourceCommandService`.

## Readiness evidence belongs to the current fixture

REQ-CR-FAULT-007: a restart assertion reads the path returned by its own completed diagnostic capture. It must never read a shared repository artifact, search for the latest log or accept evidence from another run. Per-fixture result namespaces, native log subscriptions and the HTTP readiness check of every node remain unchanged. `ClusterFixtureDiagnostics.SaveAsync` returns the saved path through `ClusterFixture.SaveFailureDiagnosticsAsync`, and `NativeRestartReadinessEvidenceAssertions` reads that exact path. The AC-CR-FAULT-001/002 operations are the regression.
