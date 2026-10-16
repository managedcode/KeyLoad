# ADR-139: Authenticated optimistic resource lifetime fences for remote reads

Status: Selected contract. Implementation and runtime qualification OPEN.

## Decision

Each selected remote or controlled data read captures its ResourceDefinition and mandatory D1 ResourceLifetimeV1 coverage in the same freshly authorized source Store.Read cut. The source cut also selects the current persisted principal, policy and physical placement. New internal V2 requests bind that exact pair to the original request, original minimum expiry, logical partition and registered physical owners. Public V1 contracts and their frozen aliases, field IDs and identity digests retain their existing bytes. Selected V2 paths never accept absent coverage or deserialize a V1 fallback.

The existing authenticated native Orleans model exchange binds the complete V2 payload, channel, caller/destination voter and actual silo address, owners, nonce and original expiry. ResourceLifetimeRead channel3 is read-only and admits exactly ordinary document, controlled document, controlled blob DATA, partition query and distributed search. ResourceLifetimeAnnRead channel4 carries native ANN reads with mandatory same-cut lifetime coverage and source authority. Native channel0 admits only queue-transfer commands; all former document/query/search/controlled-data and blob command Outcome envelopes are refused there.

Blob command OutcomeV2 uses the separate effect-sensitive ResourceLifetimeBlobOutcome channel5 and private ControlledBlobOutcomeV2 kind91. Its mandatory wrapper binds the exact source definition and D1 coverage while retaining the frozen nested V1 command/frame/result DTO bytes, aliases and field IDs. It preserves the original command effect boundary, known terminal failures and unknown-write-outcome handling; it is not admitted as channel3 read-only DATA. Selected routes do not restore V1 serving or transport fallbacks. Existing blob wire and native borrowed-fault gates use the strict V2 carriers for four DATA and six command Outcome cells, retaining the original bounded task and joined cleanup ownership. Channel selection and prepared source do not qualify whole-flow runtime behavior.

The destination validates the authenticated source route and the exact complete source definition/lifetime bijection before reading data. Source and destination have independent persisted authority; equal subject identifiers or policy epochs do not establish equivalent grants, and different valid epochs do not by themselves refuse a read. New ordinary document and query/search V2 frames carry the full source PrincipalRecord captured with that source definition and D1 pair. The same actual bounded target cut loads and charges its fresh persisted target principal, target ResourceDefinition and actual target D1 pair. The target definition remains its storage/index descriptor and policy authority; the authenticated source definition never overwrites or hides it. Source and target D1 incarnations are not compared as one identity: independently configured owners can legitimately have different original Configure operation identities.

Ordinary execution explicitly intersects both native authorization decisions. Require each side's scoped capabilities, and require each side's own field-use and schema authority before candidates, predicates, sorting, ranking or statistics. Native query schema preflight runs for both sides before either side's path-specific binding, retaining the native wildcard, system-field and EXPLAIN rules. A canonical row is visible only when both native CanReadRow decisions accept it. Existing target visibility caches retain their target identity; every accepted cached/raw record is checked against the source snapshot before query work. No source-dependent filtered set is stored under a target-only cache key.

Projection reuses the existing native projector for both authority pairs. The shared Core helper first projects the original record under the target pair, then projects that safe JSON under the source pair. Native removals cannot restore a removed value or shift array positions. Retain the complete union of original omitted paths and set redaction if either side redacted. Disclose an omitted path only when the existing schema-visibility evaluator permits it under BOTH authority pairs. Query selection/aliases apply omission relevance under both definitions. Never merge grant/role arrays into a fabricated trusted identity, substitute equality as an authority restriction, or filter only a completed payload after broader predicates/aggregates have already run.

Controlled document/blob reads retain their separate existing published-control contract: the authenticated source principal/definition is logical authority and the target's fresh persisted technical administrator admits the protected physical read. Their mandatory coverage view overlays the source definition and D1 sidecar together on the same underlying bounded target cut; unknown D1 keys cannot fall through to local sidecars. Existing destination placement/publication checks remain mandatory. Source proof does not turn caller-supplied roles or unsigned definitions into authority.

Signed inner failures reuse only the existing closed MCP safe-detail mapping. Unknown/private details are never reflected; validation accepts only its idempotent closed code/literal mapping. Error replies contain no data payload. A native literal absent from the existing mapping remains a concrete whole-flow qualification dependency; source inspection alone does not authorize an invented allowlist entry or weakened caller oracle.

The existing bounded native work owner retains the original awaited transport task, cancellation, expiry and joined shutdown. No new lease registry, membership, listener, scheduling provider or timer service is introduced. A native ZoneTree snapshot is local and is released before network waits; it is not a distributed DROP pin.

Before any caller-visible result, the original source operation passes its real RF3 read barrier and revalidates the captured lifetime, complete persisted source principal and resource policy, and physical placement in a fresh authorized source cut. Distributed search extends its existing final Revalidate/Complete capture sequence. Any changed incarnation, missing/malformed coverage or lost authority fails the whole read without a partial page or automatic retry/rebinding to a new lifetime. Existing actual cache identities remain subordinate to this final fence; no cache result can waive it.

This is an optimistic incarnation fence, not exclusive DROP exclusion. A write/retirement/name-reuse admission barrier remains a separate mandatory prerequisite. No legal DROP/recreate operation is currently implemented by this stage, so direct catalog edits cannot qualify that end-to-end gate.

## Qualification

Whole-operation tests must preserve the existing independent-epoch repair/healthy flows and cover source-narrower/target-broader and target-narrower/source-broader row, raw-use, projection, query/search/statistics refusals and safe results, known/hidden/unknown schema privacy, exact healthy literal values and projections, complete paired coverage, malformed/missing/other-resource coverage rejection, fresh persisted policy/placement invalidation after the actual target reply and before the source barrier, no partial caller result, original cancellation/expiry, bounded replay/admission and joined settlement. Reuse the existing armed NativeModelAfterDispatch probe on the same original read task; it is inactive when unarmed. Preserve original write receipts and verify healthy continuation and actual cold owner restarts through Aspire-owned RF3 with the real .NET and official MCP clients. Unit syntax checks and prepared source are not RF3 or recovery evidence. Total READ and DROP qualification remain OPEN until every selected path, including native ANN, and all mandatory gates pass.

```mermaid
sequenceDiagram
    participant Caller
    participant Source
    participant Target
    Caller->>Source: Original authorized bounded read
    Source->>Source: Same-cut definition + D1 lifetime + policy + placement
    Source->>Target: MAC-bound V2 proof, original nonce and expiry
    Target->>Target: Validate exact pair and route; bounded same-view read
    Target-->>Source: Authenticated bounded result
    Source->>Source: RF3 barrier; fresh lifetime, policy and placement
    alt Exact original authority remains current
        Source-->>Caller: Complete result
    else Lifetime or authority changed
        Source-->>Caller: Whole read failure
    end
```
