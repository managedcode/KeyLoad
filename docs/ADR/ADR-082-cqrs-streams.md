# ADR-082: CQRS streams through ManagedCode.Communication

Status: Accepted. Runtime qualification is separate from this decision.

## Decision

Use the centrally pinned `ManagedCode.Communication` packages for CQRS streams.
KeyLoad does not implement another streaming framework, producer queue, result
envelope, HTTP stream writer or command dispatcher.

The public API calls the client's Orleans connection grain. The grain executes
the authorized database operation and returns
`IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>`.
Database effects use ordinary Orleans calls to the silo-local ZoneTree owner.
Clustering and inter-silo communication belong to Orleans under
[ADR-136](ADR-136-orleans-partition-ownership.md).

```mermaid
flowchart LR
    API[SDK or MCP API] --> Connection[Orleans connection grain]
    Connection --> CQRS[ManagedCode.Communication CQRS stream]
    CQRS --> Operation[Authorized database operation]
    Operation -->|Orleans calls| Storage[Silo-local ZoneTree owners]
    Storage --> CQRS
    CQRS --> API
```

## Ownership

| Owner | Responsibility |
|---|---|
| `ManagedCode.Communication` | `CqrsStream.Create`, typed progress and results, sequence assignment, terminal outcome, capacity-one producer backpressure, cancellation-aware producer settlement and fatal failure classification. Use `CqrsStream.Normalize` when adapting an existing chunk stream. |
| `ManagedCode.Communication.Orleans` | generated Orleans serialization of Communication contracts. |
| Microsoft Orleans | Grain execution, response enumeration, batching, cancellation propagation and enumerator disposal. |
| `ManagedCode.Orleans.Graph` | Allowed grain-call transitions and their context across response enumeration. |
| `ManagedCode.Communication.AspNetCore` | CQRS HTTP stream results when an external HTTP streaming endpoint is exposed. |
| KeyLoad | Fresh persisted authorization; request/result identity; operation-specific progress validation; byte, frame, duration and concurrent-operation bounds; database effects and storage shutdown ordering. |

`ConnectionGrain.ExecuteStreamAsync` already creates its producer with
`CqrsStream.Create`. Its KeyLoad adapters must remain limited to the last row;
they must not reproduce package-owned producer or transport machinery.
Cancellation and early disposal must await the producer before releasing
resources it can still access. An in-memory response stream is not a durable job
or proof that a database write was persisted.

## Verification

Exercise actual Orleans enumeration with the published packages: successful
results, progress, authorization refusal, cancellation, early disposal, producer
failure, independent concurrent operations and joined cleanup. Exercise public
.NET SDK and official MCP clients through the Aspire-owned topology. A mechanism
test or successful build does not qualify recovery, durability or complete
client interoperability.

Confirmed defects in a ManagedCode dependency are repaired and tested in its
owning repository, published once as a complete repair batch, verified on NuGet,
and then consumed here. No local package, copied implementation or consumer
workaround replaces that delivery.

## Source basis

The installed `ManagedCode.Communication` 10.3.10 package identifies source commit
`4e654765aa72d94d0804a935031db45dd75ca4c3`. Its execution
[CqrsStream implementation](https://github.com/managedcode/Communication/blob/4e654765aa72d94d0804a935031db45dd75ca4c3/ManagedCode.Communication/Cqrs/CqrsStream.cs)
and
[producer settlement](https://github.com/managedcode/Communication/blob/4e654765aa72d94d0804a935031db45dd75ca4c3/ManagedCode.Communication/Cqrs/CqrsStreamProducerSettlement.cs)
own the streaming mechanisms above. Related KeyLoad specifications are
[RuntimeCqrs](../Features/OrleansExecution/RuntimeCqrs.md),
[ClientApi](../Features/ClientApi.md) and
[ADR-125](ADR-125-connection-owned-execution.md).
