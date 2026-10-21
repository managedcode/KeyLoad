# ADR-143: Silo count and replica count

Status: Accepted by owner direction 2026-10-17, simplified 2026-10-18 under [ADR-136](ADR-136-orleans-partition-ownership.md). Implementation under local verification; Linux qualification pending.

## Decision

Two independent startup options describe a deployment:

| Option | Meaning | Rule |
|---|---|---|
| `KeyLoad:SiloCount` | Number of Orleans silos in the cluster | Positive. Orleans owns membership and scaling; KeyLoad imposes no maximum. |
| `KeyLoad:ReplicaCount` | Number of copies of each partition's data | Positive, not larger than `SiloCount`. Default 1. |

Every silo runs the same server image with its own silo-local ZoneTree owner. A write is copied through Orleans calls to the ZoneTree owners of `ReplicaCount` silos and is acknowledged after every configured copy confirms persisted apply:

| ReplicaCount | Required persisted acknowledgements | Tolerated required-copy outages for writes |
|---|---|---|
| 1 | 1 | none |
| 2 | 2 | none |
| 3 | 3 | none |
| 5 | 5 | none |

```mermaid
flowchart LR
    Config[SiloCount and ReplicaCount] --> Cluster[Orleans cluster of SiloCount silos]
    Cluster --> Silo[Every silo: same server image and own ZoneTree owner]
    Write[Partition grain write] -->|Orleans calls| Copies[ZoneTree owners of ReplicaCount silos]
    Copies --> Ack[Every configured copy confirms persisted apply]
```

## Boundaries

- Both options are read at startup. There is no live resizing, data movement or persisted-data migration.
- Routing and validation use the actual configured copy count; three is only the default. Available owners may serve earlier committed data while a required copy is down, but ordinary and duplicate writes still require every copy.
- Local AppHost runs publish the server with the .NET SDK `PublishContainer` target when no pinned image is selected; product and comparison images use the central `Directory.Build.props` pins without a Dockerfile.
- command and result contracts preserve explicit Orleans aliases/Ids; versions require owner authorization. Persisted credentials and permissions remain database authority.
- Process recovery, real SDK/MCP operations, behaviour under faults and joined shutdown need real Aspire execution; a build or model test cannot qualify them. Exact-source Linux, recovery, cluster and endurance gates stay open until their evidence exists.
