# Fresh GitHub benchmark evidence and website publication

Date: 2026-10-02. Owner requests all performance values read from actual GitHub
comparison-job JSON and a separate post-test deployment action. Canonical slice:
BenchmarkComparisons. The earlier design task's DNS/publication exclusion remains
its historical scope; this is the owner's subsequent publication-workflow task.
DNS and independently owned runtime/schema3/README changes remain excluded.

## Problem and options

The separate pages.yml already exists, but its publish path bypasses the full
validation path, does not qualify comparison-job/artifact provenance, and omits
mandatory real-browser/native coverage inputs. The producer may upload failed
results with `always()`; artifact existence alone is insufficient. Current site
reports are schema2; unsupported future reports must fail rather than become
partial fabricated measurements.

Retain the separate CI producer and Pages consumer. Use trusted workflow code,
authenticated REST metadata, actual current-attempt comparison job/measurement
steps, immutable artifact ID and verified archive bytes. A shared complete site
qualification/build precedes a deploy-only job with Pages/OIDC rights. Performance
numbers come exclusively from copied raw JSON; charts compute from those bytes.
Publication qualifies current trusted-main website source and inspects measured
source in a separate sibling checkout. Site/measured/control SHA fields remain
accurate and independent. Candidate validation can never deploy. The owner's
latest website-only instruction supersedes the earlier equal-source task restriction;
record the conflict without deleting historical policy text.

For freshness, inspect main-push runs and attempts in descending order and select
the highest actual successful comparison job, regardless of unrelated failures.
Failed/partial reruns cannot erase an earlier successful comparison attempt.
Authenticated retained attempt history is required; inaccessible history fails.
After selecting success, invalid/missing artifact or reports fail without fallback.
Recheck both current trusted-main website SHA and the immutable measurement tuple
before deployment. Record check time without claiming an atomic lock over CI.

## Safe independent work and open implementation decision

Strongest model owns architecture, acceptance, decomposition and final review.
After contracts are frozen, a bounded worker can own new Node GitHub metadata/
digest gates and acceptance tests; a disjoint scope can own real archive input
qualification. Root owns workflow, source-revision environment, coverage inventory,
policies and durable integration docs. Existing builder repair workers finish
their current scopes before reuse; no same-file overlap.

Download the immutable archive once and verify its REST SHA256. The pinned official
download action only warns on digest mismatch, so its success alone is inadequate.
Strongest review must freeze extraction/byte-equality through existing .NET BCL,
complete preflight confinement/bounds, exact file sets and real TUnit proof. No
custom ZIP parser, fake GitHub transport, credential exposure or new installation.
Controlled metadata/corrupt archive inputs prove rejection, never API/publication
success. Real automatic callback, complete GitHub gates, Pages provider receipt
and live report/UI verification remain required operational evidence.

## Actual G browser-coverage finding

The exact G GitHub suite passed all70 authored functional tests, but its coverage
join rejected the first real Chrome delta: `{"result":[],"timestamp":867.18667}`.
The snapshot was taken on the initial blank page before navigation. The official
[CDP Profiler contract](https://raw.githubusercontent.com/ChromeDevTools/devtools-protocol/master/pdl/js_protocol.pdl)
returns an array for the current isolate and resets counters; it does not promise
a nonempty delta. Later seven snapshots map authored website execution. Numeric
thresholds already pass; this is a native-protocol interpretation error.

Skipping startup collection or filtering empty files loses original evidence and
does not handle other legitimate blank/no-script windows. Keep every native byte
and hash. Accept only a structurally valid empty browser array as zero coverage;
preserve Node-empty/malformed rejection and strengthen each browser session to
require mapped authored functions. Empty-only or unmapped-only sessions must fail
even beside another valid session. Root records the actual red artifact first;
strongest review freezes disjoint converter/reader and realistic regression scopes
before bounded workers, followed by full exact-source GitHub qualification.
