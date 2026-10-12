# Native movement process entry observation

Future preview only. Refines original process recovery flow without redispatch, alternate topology or snapshot-threshold changes.

REQ-MOVEMENT-PROCESS-ENTRY-001 / AC-MOVEMENT-PROCESS-ENTRY-001: original six-host native command replies do not imply every physical follower has applied the leader acknowledgement. Before node0 seed/blob/prepare/healthy journal observation, await its EXISTING actual Coordinator.ReadBarrierAsync under the original cancellation. Native follower barrier authenticates the leader receipt and directly joins SAME local Materializer.WaitForApplyAsync. Then acquire original Log.ProtocolGate, perform the unchanged bounded native retained-entry scan, and release in unconditional finally. No operation is resent and no outcome/entry is synthesized.

REQ-MOVEMENT-PROCESS-ENTRY-002 / AC-MOVEMENT-PROCESS-ENTRY-002: retain every original literal receipt/token/mutation/model/blob/phase/outcome/fullcut/cold and healthy assertion, original six hosts, real process termination, original deadline and joined cleanup. Missing or snapshot-covered entries remain failures. Published checkpoints legally make Log.ReadEntry(index<=Snapshot.Index) unavailable; a checkpoint image is NEVER called a retained tail entry.

Original R18 failure: GenuineSixHostOriginalPrepareSurvivesKilledOwnerAndColdOutcomeObservationWithoutRedispatch failed after15431.807ms with original Missing at ProcessEntry.Read35→SeedAsync25→WarmAsync73. Its original artifact does not record the local committed/snapshot cut; stale follower observation is source-proven possible, exact observed cause remains unproven until genuine rerun. Default SnapshotThreshold1024 unchanged. Existing ProcessCapture requires complete retained1..Committed history and independently refuses any legitimate checkpoint coverage; this limitation remains OPEN, not hidden by this narrower barrier repair.

Maps the SAME original existing TUnit case. Compiler, Linux process qualification and full cold/runtime gates remain OPEN. Native protocol gate does not freeze file-I/O snapshot publication; covered/missing evidence still refuses.

```mermaid
sequenceDiagram
    participant Caller
    participant Leader
    participant LocalFollower
    Caller->>Leader: original signed native command
    Leader-->>Caller: actual quorum applied reply
    Caller->>LocalFollower: existing native read barrier
    LocalFollower->>Leader: original authenticated barrier RPC
    Leader-->>LocalFollower: actual committed receipt
    LocalFollower->>LocalFollower: SAME materializer apply join
    Caller->>LocalFollower: bounded native retained entry read
```
