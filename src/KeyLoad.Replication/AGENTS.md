# KeyLoad.Replication

## Purpose, entry points and ownership

- Owns the replica-copy path: copying committed records to the `KeyLoad:ReplicaCount` silo-local ZoneTree owners through ordinary Orleans grain/Grain Service calls with majority acknowledgement, plus the durable replica log and recovery behind it.
- Main sources: `ClusterReplication/` and `StorageRecovery/` in this project.
- Replication feature code belongs under `<SliceName>/` with matching `docs/Features/<SliceName>.md`; this project does not own Orleans activation routing or node-local physical storage.
- Microsoft Orleans does all clustering (membership, discovery, failure detection, directory, placement, activation migration, communication); this project adds none of its own and holds no consensus, leaders, votes, peer transport or peer authentication. The silo-local ZoneTree owner (node-local `PartitionHost`) owns storage, journals, file locks and apply gate; files never move with activations. See [ADR-136](../../docs/ADR/ADR-136-native-orleans-partition-ownership.md).
- Owner-authorized applicable skill: global [Orleans](/Users/ksemenenko/.codex/skills/orleans/SKILL.md) 3.1.1; apply its state ownership and failure-model guidance. The prior bootstrap prohibition remains for other unapproved installations.
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Ordering, majority acknowledgement, log durability and recovery are protected boundaries. Qualification requires exact GitHub Actions artifacts and topology evidence.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore --configuration Release -warnaserror
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
