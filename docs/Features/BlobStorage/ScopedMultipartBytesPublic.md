# Scoped multipart byte isolation through bounded cleanup and cold restart

The acceptance code is authored; compiler, runtime and Linux RF3 qualification remain pending. Canonical BlobStorage, REQ/AC-BLOB-001/002/003/004/007 and ADR-038. Strengthen the existing BlobScopedUploadIdentityPublicTests no-Args case; preserve its original empty-upload flow first. No added Test, protocol, provider, dependency, capacity benchmark or private storage inspection.

- REQ-R42-BLOB-SCOPED-BYTES-001 / AC-R42-BLOB-SCOPED-BYTES-001: four actual full BlobRefs differ only by partition key, resource or name and share one UploadId. Each stores two genuine parts, length65553, independently literal byte(scope,position)=(position*17+scope*31+7)%251. Original Begin/Part/Complete results bind independently prepublication SDK/MCP placement and exact full public value fields; timestamps/seeds remain authentic retained opaque server fields.
- REQ-R42-BLOB-SCOPED-BYTES-002 / AC-R42-BLOB-SCOPED-BYTES-002: exact first4, cross-boundary offset65534/count6, tail65536/count17 and EOF65553/count0 through both real SDK and official MCP are independently sliced from the fixed corpus. Full metadata, upload identity, offset and bytes match; fifth absent scope remains absent. No materialization above one65536-byte part in a write or range result.
- REQ-R42-BLOB-SCOPED-BYTES-003 / AC-R42-BLOB-SCOPED-BYTES-003: abort and bounded MaxParts1 first/final reclaim only the first active scope. Literal65536 then17 released bytes remove only its chunks; remaining three active uploads preserve independent complete state and later publish their own bytes. Removed original first command lifetimes strictly refuse, while each sibling original result replay is immediately followed by all-scope full current observations. No cross-call atomic cut, raw canonical journal, private quota or no-append claim.
- REQ-R42-BLOB-SCOPED-BYTES-004 / AC-R42-BLOB-SCOPED-BYTES-004: join original owned callers before two actual all-three cold cycles and use fresh callers; exact sibling bytes/full original receipts and removed-scope fences persist. Existing original deadline and ordered body/disposal/restart failure ledgers remain unchanged. Compiler, runtime and Linux RF3 qualification remain pending.

```mermaid
flowchart LR
 A[Four qualified scopes share one UploadId] --> B[Two distinct literal parts per scope]
 B --> C[Abort only first and reclaim one part at a time]
 C --> D[Sibling complete publications and independent ranges]
 D --> E[Each replay or refusal followed by all-scope state]
 E --> F[Join callers and two all3 cold cycles]
```

Coverage distinction: ScopedUploadIdentity.md explicitly covers empty objects and excludes cold cycles/byte isolation. MaximumRange covers one object and maximum unaligned boundaries. Original R40 PartId checks same-command changed-content conflicts within one full BlobRef. This continuation qualifies full-scope chunk/read isolation with fixed nonempty independent corpora and cleanup; it does not repeat those criteria.

Canonical roles: IntegrationTests BlobStorage Models/State, Helpers/Corpus/Seed/Publication/Cleanup/Replay/Trial and Assertions. Existing Case strengthened, no new Test/UID or argument. Public timestamp/initial integrity seed are retained authentic opaque metadata, not a client-clock oracle; part SHA and subsequent72-byte chain are independently calculated from fixed bytes. Full original five-field SDK lifetime Problem is retained from first genuine refusal and exactly compared thereafter; all five fields are independently native Errors.Problem(TokenInvalidated, "The selected upload lifetime is unavailable.") from the authenticated current BlobErrors.Token body. Removed-scope final Reclaim is valid exact replay; nonterminal removed-scope receipts refuse. No normalization of observed values.

Eight fixed per-scope/per-part SHA256 literals are independently calculated and checked against each generated literal part before submission. The first Abort and partial Reclaim receipts replay while their lifetime exists; after final cleanup the old nonterminal Reclaim is fenced, while final Reclaim remains exact.
