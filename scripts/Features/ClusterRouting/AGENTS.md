# ClusterRouting scripts

## Purpose and entry points
- Produce and independently verify the genuine same-data-epoch RPC1/peer2 server image used by C1's actual Aspire RF3 protocol tests.
- Entrypoints: prepare-rpc1-server-image.mjs and verify-rpc1-server-image.mjs. The accepted contract is docs/Features/ClusterRouting/NativeCqrsRequestV2.md and ADR-082.

## Boundaries and ownership
- Read root AGENTS.md and scripts/AGENTS.md first. Keep all work in the canonical ClusterRouting slice.
- Root freezes source identity, receipt schema/environment names, CI and AppHost joins; workers return private patches before root review and application.
- Reuse existing bounded native Git archive/inventory and BenchmarkComparisons image engine, manifest and owned-registry helpers. Preserve the separate native5 storage-upgrade implementation and originals.
- The fixed image-source revision is377886f35928866f083806062b446056d64539e3. Require its exact tree/source/archive inventory and zero overlays, separately from the current producer's real Linux GitHub job identity.
- Never invent GitHub environment, runtime image/manifest, native source, package, test or qualification evidence. Preserve secrets and report only controlled failure categories.

## Commands, skills and risks
- node --check validates script syntax only. Runtime script regression callers use TUnit through the root's actual Aspire unit entry; image production qualification runs in the real Linux docker-rf3 CI job.
- No installed script-specific skill or tool installation is required. Follow the already applicable Aspire/Orleans guidance for root integration.
- Every owned export, process, registry use and file handle has bounded admission and joined cleanup. Retain original manifest/receipt/archive/inventory artifacts and actual producer/run identity; no timeout-abandoned operation or mixed-source proof may count as passing.
