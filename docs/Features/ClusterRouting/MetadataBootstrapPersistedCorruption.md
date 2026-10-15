# Persisted metadata bootstrap corruption refusal

This KL-018 operation extends REQ-META-BOOTSTRAP-001/002 and AC-SCAT-002 with a
whole native ZoneTree operation. It reuses the real current bootstrap, ordered
apply, persisted administrator, document/event/queue and outcome oracles. No
production API, fallback, transport, automatic repair or test phase hook is added.

```mermaid
flowchart LR
 A[Native bootstrap and committed triad] --> B[Capture original catalog and outcomes]
 B --> C[Test-owned malformed catalog bytes]
 C --> D[Read and new bootstrap refuse corruption]
 D --> E[Same-root reopen and same refusals]
 E --> F[Undo only injected fault with exact original bytes]
 F --> G[Same previously refused command succeeds]
 G --> H[Original receipt replay and two joined cold owners]
```

AC-META-BOOTSTRAP-CORRUPTION-001: unsupported native catalog version, empty bytes
and 8193-byte row each refuse both native catalog read and a fresh actual signed
bootstrap operation as Corruption with the independent literal detail. Before
and after a genuine same-root native reopen, the entire bounded image, complete
store identity, exact native position and all earlier bootstrap/data outcome
bytes remain unchanged; the refused command has no global persisted outcome.
Corruption must not be converted into a definitive ordinary failed command.

AC-META-BOOTSTRAP-CORRUPTION-002: undo only the test-owned injection using the
exact captured original catalog bytes. This is a fixture fault rollback, not an
authorized operator repair or product recovery claim. The same originally
refused operation then succeeds and persists its complete independently checked
policy-epoch3 outcome. Exactly three commits follow the original cut: injected
fault, exact fault undo, and the successful bootstrap. Literal triad records,
original outcomes/receipts, current authorization fences and bounded complete
image remain conserved through replay and two further genuine native reopens.

Declaration: MetadataBootstrapPersistedCorruptionTests.
NativeCommittedCatalogCorruptionRefusesNewBootstrapThenExactFaultUndoAndTwoColdOwnersPreserveOriginals
has three explicit argument cases. Compiler/discovery/runtime proof is OPEN.
This native helper scope does not qualify elected-leader loss, full-server cold
readiness, public SDK/official MCP interoperability, Linux ABI/provider, power-loss,
endurance or full KL-018 acceptance. Existing real RF3 declarations remain required.

The original R259 real native run rejected all three corrupt forms; its empty-byte variant failed only because the test expected the catalog shape message. Empty bytes are rejected earlier by the unchanged native binary decoder with `Corruption` and `The internal binary payload is invalid.`; a decoded unsupported catalog version and the catalog byte ceiling keep the exact catalog-malformed message. The complete operation asserts the independent literal message for each injected fault at both live and reopened owners, retaining the exact error code, absent attempted outcome, unchanged corrupt image/cut, original outcomes, exact injected-byte undo, same command continuation/replay and two healthy cold owners. No product error or decoder normalization is changed.
