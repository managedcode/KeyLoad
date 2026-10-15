# StorageRecovery native publication contract

- Owns only the shared physical-storage publication contract for ADR-136.
- The implementation is the same silo-local `PartitionHostApplicationOwner` in KeyLoad.Server; this contract never creates a store or another owner.
- Native storage Grain Service initialization initiates the original opening. Other native services observe `Opening` or await `WaitForOpenAsync` without initiating acquisition.
- Caller cancellation cancels its wait, not the original physical opening. A null `Opening` means initialization never started.
- Keep file handles, storage recovery, locks and joined close in the node-local owner; Orleans activation placement does not move them.
- Follow root and Clustering policy. Main owns coherent solution integration/build/TUnit/Aspire RF3/Linux evidence; source preparation does not qualify execution.
