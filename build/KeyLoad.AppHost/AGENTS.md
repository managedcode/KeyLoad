# KeyLoad.AppHost

## Purpose, entry points and ownership

- Owns the primary KeyLoad development and functional-test Aspire host: AddProject<KeyLoad> services using the published in-memory RapidMembership Orleans provider.
- Entry point: `Program.cs`. Selected comparison database infrastructure belongs to `../KeyLoad.LoadTests.AppHost`; its workload runs in the separate client process, outside the resource graph.
- Keep host wiring and genuinely shared infrastructure composition here. Feature-specific deployment behavior belongs under `<SliceName>/` and the matching `docs/Features/<SliceName>.md`.
- Aspire owns resource orchestration, not database storage state. Each silo's ZoneTree owner (node-local `PartitionHost`) owns its storage, journals, file locks and the apply gate; Microsoft Orleans does all clustering and Orleans grains route commands (ADR-136).
- `Program.cs` composes real project services. Each silo retains its own ZoneTree storage lifetime under the current storage-mode contract; copy count is independent of silo count. Orleans owns membership and communication. Do not retain a second peer protocol or coordination-only database nodes.
- `KeyLoad:StorageMode` selects `IsolatedVolumes` (default, `DataRoot/<silo-name>`) or `SharedVolume` (all nodes receive `DataRoot/shared`). SharedVolume requires an explicit copy count of one; reject conflicting counts. Configuration and common paths do not prove single-owner opening, joined ownership transfer or the deployed volume's lock/flush behavior.
- Ordinary build, development, tests and load execution never publish or start a KeyLoad server image. Keep SDK PublishContainer at explicit release/publication only. AppHost-owned abrupt faults validate real PID plus Aspire/OS starts, kill the selected owned process tree and join its actual exit.
- Give every silo a private membership TCP endpoint and one shared, independent secret parameter. Membership discovery uses the provider's localhost/interface discovery; do not supply seed IPs or membership storage. Dispose only resources owned by the run. Capture actual source/DLL/PDB/process identities; do not substitute Docker image/PID fields. Runtime, process recovery, matched-resource qualification and public measurements remain separate gates.
- Apply the installed Orleans and Aspire skills within their actual ownership boundaries.
- Preserve write-only secrets, endpoint discovery, resource health dependencies, scoped fault injection, joined shutdown and the configured Orleans topology. Local results do not qualify delivered-source Linux or public performance gates.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
