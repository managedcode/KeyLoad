# Internal serialization acceptance

The current design is [ADR-060](../../ADR/ADR-060-internal-serialization.md).
Orleans owns binary serialization. KeyLoad owns application identity, domain
validation, resource admission and effects. Public protocols and opaque storage
bytes retain their actual contracts.

| Requirement | Observable acceptance | Current verification |
|---|---|---|
| REQ-IS-001 / AC-IS-001 | Active attributed DTOs keep generated aliases and field IDs; actual values round trip. | Contract-family and normal/scalar tests. |
| REQ-IS-002 / AC-IS-002 | Native decoding preserves actual values; application envelope version, required root and complete input are checked. | ServerContractSerializationTests, boundary, malformed-reader and envelope tests. |
| REQ-IS-003 / AC-IS-003 | Persisted values and accounting use the same generated encoding; exact user content is retained. | Whole-operation storage and accounting tests. |
| REQ-IS-004 / AC-IS-004 | Current storage identity, integrity and unsupported versions reject before effects. | Real identity, metadata and recovery tests. |
| REQ-IS-005 / AC-IS-005 | Payload and per-operation byte bounds reject before effects and preserve healthy follow-up. | Real bound/accounting and process tests. |
| REQ-IS-006 / AC-IS-006 | Typed Orleans requests, replies and principal binding execute through actual silos. | Native grain tests and Aspire SDK/MCP cases. |
| REQ-IS-007 / AC-IS-007 | Signed claims retain their current version and reject tampering or foreign authority. | Actual claims and authorization tests. |
| REQ-IS-008 / AC-IS-008 | Unsupported application/storage versions have no reader conversion path. | Current-format refusal and recovery tests. |
| REQ-IS-009 / AC-IS-009 | Delivered source has authentic Linux build, functional, recovery and coverage evidence. | Separate GitHub qualification gates; focused local passes do not close them. |
| REQ-IS-010 / AC-IS-010 | Runtime measurements precede optimization and retain original source/workload evidence. | Separate native serialization diagnostics. |

Private wire-parser and custom DOM-converter tests are retired with those
implementations. Native field evolution is the Orleans contract. Existing domain,
authorization, integrity, resource and failure assertions remain required.
No test result in this document establishes power-loss or production readiness.
