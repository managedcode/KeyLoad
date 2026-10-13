# Movement transfer request codec ownership

REQ-CR-CODEC-TRANSFER-001 refines REQ/AC-ROUTE-005 and the native movement transport contract. The native movement transfer read builders belong to a feature-local readonly value helper. The shared GrainRequestCodec retains issuance and its existing database, clock and validated options; it borrows them through MovementTransfer without owning storage or adding a transport.

AC-CR-CODEC-TRANSFER-001: preserve all five typed builders and their six actual server call sites. Each original request ID, principal, read kind, purpose, native payload and original expiry reaches the same GrainRequestEnvelopeConstruction.MovementRead and GrainRequestCodec.Issue. Serialization precedes the same single clock read, expiry and byte-budget checks remain exact, and no public or persisted serializer alias/field ID changes. Native references, original method bodies and a guarded combined patch provide source proof; compiler and real movement operation flows remain required.

AC-CR-CODEC-TRANSFER-002: the enforced aggregate GrainRequestCodec code-line limit stays at 200; extract the transfer responsibility rather than deleting behavior or suppressing KLD0031. The readonly helper is used directly without boxing or a newly retained heap owner. Existing ClusterRouting movement unit, process and public RF3 flows remain the behavioral regression inventory; source extraction does not claim a measured allocation or performance improvement.

```mermaid
flowchart LR
 A[Six existing server call sites] --> B[Codec MovementTransfer value helper]
 B --> C[Five unchanged typed builders]
 C --> D[Native payload and original expiry]
 D --> E[Existing envelope construction and signed issuance]
```

Canonical slice map: KeyLoad.Orleans Features/ClusterRouting/Serialization and KeyLoad.Server Features/ClusterRouting/Execution; existing ClusterRouting tests and this feature subdoc. Source integration and the complete local Release build passed at the R36 checkpoint, including the compiler code-line limit. Native discovery completed in the R36 local cohort; the two original movement flows failed. Process/Linux RF3 and adverse cleanup qualification remain OPEN.

REQ-CR-CODEC-PLACEMENT-001 → AC-CR-CODEC-PLACEMENT-001: the existing CreatePlacementIntentPhase signature and all callers stay intact while its envelope construction moves into a distinct feature-local static PlacementIntentRequestEnvelopeFactory. Preserve the one clock read, original expiry check before operation authority verification, exact kind refusal, native verified payload copy, original immutable protocol purpose and the same final signed issuance. No new storage, transport, format or policy is introduced. Frontend and new public contracts are N/A because this changes internal codec ownership only.

The transfer-only source stage projected 202 aggregate codec code lines and does not meet the 200-line acceptance gate. The minimal placement factory successor projects 188 from the frozen 226-line native body inventory without deleting builders, suppressing diagnostics or compressing source. The final native source body has 188 code lines and the complete local R36 Release build passes the unchanged 200-line compiler limit. The transfer-only 202-line intermediate stage remains superseded. Original movement operation flows, Linux qualification and measured performance remain OPEN.


