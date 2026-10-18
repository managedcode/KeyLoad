# Claim and ACK process ownership and joined output

REQ-MSG-001 and AC-MSG-001 require original process recovery evidence, including the real original child and both redirected pipe readers. The existing MessagingCrashTrial read only one stdout marker and waited for process exit. It did not continuously drain stderr/stdout or join both readers before storage reopen and cleanup.

REQ-MSG-PROCESS-JOIN-001: Reuse the existing native CommandIdempotencyProcessChild and its actual bounded pipe-reader/marker APIs. Start both original readers immediately, observe the existing CRASH marker, kill the original whole process tree, join the original exit and both reader tasks, then retain the existing file-lock readiness and real recovered database verification. No second runner, synthetic marker, new process timeout, fault stage, storage format, retry or fake caller is introduced.

AC-MSG-PROCESS-JOIN-001: All original MessagingCrashTrial cases plus new QueueClaimAckProcessRecoveryTests retain their original native mode/stage/mutation-index identities. Receive/ACK × HeaderWritten, PayloadWritten, JournalFlushed, MutationApplied and ApplyCompleted exercise the original canonical child. Bounded output/EOF/reader/exit/startup failures remain actual failures. The existing native child closes only after settlement; existing30-second cleanup preserves original plus cleanup failures and retains an unsettled trial root. Storage readiness and release precede deletion. The operation90-second budget and8192-character output bound remain explicit. Current functional caller cancellation is passed unchanged; process kill does not prove power-loss durability.

The change lives in the RecoveryTests Messaging crash trial; the existing DocumentStorage process helpers are reused unchanged and the real five-stage messaging cases qualify this integration.

```mermaid
flowchart LR
 Start[Native original child] --> Pipes[Both bounded native output readers]
 Pipes --> Marker[Original CRASH marker]
 Marker --> Kill[Kill original process tree]
 Kill --> Join[Join original exit and both readers]
 Join --> Readiness[Original locks released]
 Readiness --> Verify[Real recovered claim and ACK state]
 Verify --> Cleanup[Native joined cleanup then owned root removal]
```
