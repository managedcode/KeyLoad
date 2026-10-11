# TestInfrastructure local image tooling

- Owner correction 2026-10-11 requires native SIMD to remain enabled. The native selector rejects retired `unit-scalar` selections and inherited options that disable CPU intrinsics or SIMD before launching a runner. Preserve native TUnit50, every ordinary suite and authentic source/image receipts; no active disabled-intrinsics profile is permitted.

- This slice owns only the explicitly selected local RF3 test-image producer and its bounded ownership receipts.
- Never create Docker database nodes here; only the Aspire-owned RF3 child AppHost composes node resources.
- Do not consume or synthesize GitHub source/run identities, registry manifests, qualification receipts, or benchmark evidence.
- Hash the actual supported Dockerfile context inputs and fail closed on unsupported ignore rules, links, input mutation, excessive files/bytes, ambiguous Docker identity, and unknown producer actions.
- Own only the unique image tag supplied by the Aspire AppHost. Cleanup must revalidate source/invocation labels and image ID, prove no matching container exists, and invoke image removal without force/prune. Preserve receipts and all unrelated Docker resources.
- Every subprocess has a bounded deadline, bounded drained output, cancellation settlement, and joined readers/process before the producer exits.
