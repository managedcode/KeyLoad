# TestInfrastructure local image tooling

## Purpose, entry points and ownership

- This slice owns only the explicitly selected local cluster test-image producer and its bounded ownership receipts.
- Never create Docker database nodes here; only the Aspire-owned cluster child AppHost composes node resources.
- Do not consume or synthesize GitHub source/run identities, registry manifests, qualification receipts, or benchmark evidence.
- Hash the actual supported Dockerfile context inputs and fail closed on unsupported ignore rules, links, input mutation, excessive files/bytes, ambiguous Docker identity, and unknown producer actions.
- Own only the unique image tag supplied by the Aspire AppHost. Cleanup must revalidate source/invocation labels and image ID, prove no matching container exists, and invoke image removal without force/prune. Preserve receipts and all unrelated Docker resources.
- Every subprocess has a bounded deadline, bounded drained output, cancellation settlement, and joined readers/process before the producer exits.

## Boundaries and protected risks

Follow the [root policy](../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
node scripts/Features/RepositoryGovernance/verify.mjs
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
