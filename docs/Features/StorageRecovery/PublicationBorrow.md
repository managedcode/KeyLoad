# publication borrowing

REQ-EXECUTION-PUBLICATION-001: resolving the same silo-local physical publication through DI must not acquire another physical owner or transfer disposal ownership. grain services keep the original `IPartitionStoragePublication` interface, original opening task, cancellation and initialization sequence. A cached `Func<IPartitionStoragePublication>` borrows the concrete owner; its registration captures that same owner once inside the delegate factory, while the returned delegate is not disposable.

REQ-EXECUTION-PUBLICATION-002: normal lifecycle keeps DurableJobs at Active before the late physical close. Generic DI disposal must not bypass that ordering by capturing the physical owner again through a later publication alias. No additional scheduler, preparation lease, manager wrapper or public contract is introduced.

```mermaid
flowchart LR
  C[Concrete application owner] --> S[Silo construction]
  S --> G[GrainService Init]
  G --> F[Borrowed publication delegate]
  F --> O[Same original opening task]
  D[Reverse DI disposal] --> N[Silo lifecycle join]
  N --> P[Late physical close]
```

AC-EXECUTION-PUBLICATION-001 maps to `PartitionStoragePublicationBorrowTests.UnstartedContainerDisposalPreservesBorrowedOwnerAndCompleteCanonicalCommitAcrossTwoColdOwners`: a test-owned real physical owner is registered as an externally owned instance with actual `HostBuilder.UseOrleans` and production `PartitionStorageRegistration.Register`. Resolving publication returns that exact owner and original opening task. Disposal of this unstarted container must leave the external owner open. A genuine atomic canonical byte commit, exact value, position, complete bounded rows and full identity are then retained through two joined same-root cold owners; final known shutdown permits root removal. Unknown acquisition/close or failed assertions retain the original root and all body/cleanup failures.

This is an actual DI/physical-storage regression, not a started-silo, job settlement, cluster acknowledgement, public SDK/MCP, power-loss, performance or readiness proof. The fixed private row is a storage control, not an independently acknowledged database operation. Existing constructor-cycle cancellation and original storage/application-disposal whole cases remain required. Exact-source execution is a separate gate.

The causal source investigation is retained separately: the former interface factory returned the same IAsyncDisposable owner after Silo capture; DI could capture it a second time and dispose it before lifecycle. barrier/read locks protect their active operations but do not prove an unregistered preparation continuation joined. This change preserves existing lifecycle ownership instead of adding a parallel drain mechanism.

Applicable decisions: [ADR-136](../../ADR/ADR-136-orleans-partition-ownership.md), [ADR-117](../../ADR/ADR-117-tunit-ci-entry.md). Feature: [StorageRecovery](../StorageRecovery.md).
