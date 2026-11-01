# ADR-023: Separate lifecycle and authority for journals, events, CDC, and queues

Status: Accepted; complete all-capability retention and restore qualification pending.

## Context and decision

ZoneTree's write-ahead log supports database recovery; domain event streams are user business history; the committed projection outbox supplies change/projection work; queue and subscription state own delivery progress. They differ in authority, retention, acknowledgement, and public visibility. Trimming one must never silently erase another's promised history.

Keep these records as separately named logical authorities, even when they share a physical store or atomic apply. ZoneTree owns its write-ahead log and segment cleanup; public event retention follows stream policy; outbox cleanup follows consumer/rebuild pins; queue cleanup follows delivery/retention rules. A store backup holds every state class of that store ([ADR-011](ADR-011-current-format.md)).

```mermaid
flowchart TB
    Mutation[Canonical transaction] --> WAL[ZoneTree write-ahead log]
    Mutation --> Event[Domain event history]
    Mutation --> Outbox[Committed projection outbox]
    Mutation --> Queue[Queue and delivery state]
    WAL --> Recover[Local engine recovery]
    Event --> Public[Authorized event API]
    Outbox --> Projection[Consumer checkpoint and retention]
```

## Alternatives and consequences

Treating every ordered record as one interchangeable log is rejected because its GC rules and access policies differ. Shared physical storage reduces overhead, but separate keyspaces, ownership and tests remain mandatory. Operators must receive explicit history availability and corruption errors.

## Related requirements and implementation contract

Related: `REQ-FEED-001/AC-FEED-001`, `REQ-EVENT-003/AC-MP-005`, `REQ-MSG-003/AC-MSG-003`, `REQ-BACKUP-004/AC-BACKUP-004`; ADR-003/008, ADR-024/025/027/030.

1. Freeze record authority, lifecycle, and retention owner for each state class under the current storage schema.
2. Test independent trim, corruption, backup/restore, reopen and consumer pin boundaries against a real store.
3. Implement catalog/keyspace and recovery owners in their canonical slices; atomic writes include every affected state and receipt.
4. Reject unsupported persisted versions; never repurpose one log's sequence as another public cursor.
5. Qualify local recovery and backup/restore through GitHub process and replicated-cluster suites; separately qualify retention and endurance.

Current source: `src/KeyLoad.Storage.ZoneTree/`, `src/KeyLoad.Core/ChangeFeeds/Execution/ProjectionOutbox.cs`, `Events.cs`, `Messaging.cs`, `EventSources.cs`; tests include ChangeFeed, EventStreams, Messaging, StorageRecovery and BackupRestore suites. These paths are not proof of full capability qualification.
