# Native membership provider row preservation

TASK-KL036-NATIVE-PROVIDER-ROW-001 supports the current native six-silo membership gate. The original ActualSixNativeSilosRefuseWrongNamesAndCancellationThenRepairAndFenceRestartedGeneration Case and its deadline, owner topology, refusal controls, address fingerprint and joined shutdown remain unchanged.

## Requirements and acceptance

- REQ-NATIVE-PROVIDER-ROW-001 / AC-001: read the actual Aspire-configured AzureBasedMembershipTable via IMembershipTable.ReadAllAsync and ReadRowAsync. Capture every one of MembershipEntry's eleven typed properties, row ETag and table version/ETag. Each current handle independently determines expected full native address, silo name and Active status. A ReadRow result must contain exactly its requested address.
- REQ-NATIVE-PROVIDER-ROW-002 / AC-002: after each original wrong-name, maximum-row, maximum-address and cancellation refusal, observe the full provider again. Compare captured stable address/name/host/role/proxy/fault-zone/update-zone/start fields against the original provider observation, and require all current handles remain Active. These original admission paths are read-only. Healthy ReadView and its original complete six-row fingerprint assertions remain in place.
- REQ-NATIVE-PROVIDER-ROW-003 / AC-003: after the original genuine silo restart and liveness join, require the old generation has no Active persisted row; allow native cleanup to remove that row. Require each new current handle's complete captured row and exact current full address/name/Active status through both ReadAll and ReadRow. Compare stable metadata between those reads; do not assert old-generation row retention.
- REQ-NATIVE-PROVIDER-ROW-004 / AC-004: detach mutable suspect-list observations into typed arrays. Heartbeat time, suspect times, row ETag and table version are live observations, not byte-equality or no-write assertions. Status is independently asserted against the stabilized current handles, not compared as a fixed historic byte image.

## Contract and qualification boundary

The selected native Orleans 10.4.0 provider has real background heartbeat and membership lifecycle writes. Initial host/role/proxy/zones/start metadata are preservation observations, not independently configured literal values. All fields are captured; this does not claim independent initial goldens for fields not exposed by the test's handles. The retired signed-HTTP heartbeat authorization criterion is retired transport history; native callee-current-address admission and stable persisted provider-row preservation support the replacement criterion. They do not prove the former HTTP401 contract or direct UpdateIAmAlive authorization. Main's canonical registry owner decides the final mapping.

No direct provider mutation, nonexistent-row negative, new test/UID, retry, timeout or transport is added. Frontend/public SDK/MCP/SQL are N/A for this native provider supporting stage; their full database operation gates remain separate. Aspire owns the real AzureTable infrastructure and native silos. The existing fixture owns application/cluster cleanup and all failures.

## Flow

```mermaid
sequenceDiagram
    participant Test as Original six-silo Case
    participant Provider as Native Azure membership provider
    Test->>Provider: ReadAll and each current ReadRow
    Test->>Test: Original read-only refusals and cancellation
    Test->>Provider: Full typed rows and stable preservation after each refusal
    Test->>Test: Original restart and liveness join
    Test->>Provider: Full current rows; old generation not Active
```

Source draft only. Fresh current guards, native NF/existing N/creation admission, compiler, analyzer, unchanged-deadline runtime and exact-source Linux qualification remain OPEN until actual evidence is obtained. This stage is not whole KL036 acceptance.
