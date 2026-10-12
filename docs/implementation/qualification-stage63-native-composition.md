# Stage 63 — native SQL composition and caller integration

The original plan remains **18 accepted of 104**, with **86 in progress**. KL-105 is a separate additional profiling task. No acceptance state changes are justified by this source integration.

## Integrated implementation

Typed queue-to-graph composition, left-filtered joins, queue EntityRef joins and right-alias refusal now share the current source. Native remote ANN ownership, read-cut/index-wait witnesses, replica snapshot readiness, submission outcome authority and queue repair/replay are integrated with their complete operation scenarios. The query cursor keeps its existing generated claims, signatures and verification order under a feature-local owner.

Compiler repairs retain actual existing APIs, captured Orleans execution context and cleanup of original test fixtures. The current HTTP admission uses standard ASP.NET Core authentication and authorization; database operations still check fresh persisted permissions.

## Evidence and remaining gates

Source integration receipts are retained in `TestResults/stage61-native-join`. Current compiler/analyzer observation, the full Release R19 build and the focused current-image Unit/Recovery operation results must be recorded before reporting their outcome. Coverage remains unmeasured. SDK/MCP RF3, Linux qualification, endurance and performance acceptance remain open.

The prior source `43117375a574a43fda55c8cdb812d3c79755fab0` Linux run `38206673070` completed with five successful and thirteen failed jobs. It does not qualify this subsequent source.

KL-105 has verified the installed diagnostic tool and profiling skill. No real database process was available in the initial native discovery; actual bounded before/after captures and matched Linux performance results remain open.

## Concrete SDK repair

The SDK and MCP catalog already declared WaitForAnnIndex, but SearchApi lacked its HTTP mapping. The current server now maps the existing protocol route and typed request to the existing authorized Orleans read dispatch. Original native SDK/catalog/route-reference evidence is retained in ann-http-r105-wait-route.native-originals.json. Execution proof remains pending.

Canonical whitespace formatting passed for 190 changed C# files. The native compiler/analyzer observations moved from 251 to 49 to five errors, with zero warnings in the last observation. The final five Unit using/disposal diagnostics were repaired without dropping operation checks; the subsequent full build remains the verification gate.

## Actual R19 results

Full normal Release passed with zero errors and warnings in286.77 seconds; all9353 tracked/nonignored input hashes stayed unchanged. The single focused Unit run executed256 native cases:250 passed, six failed, none skipped in37.9 seconds. The original176 cases remain present. Native parallelism was configured50; overlapping original TRX timestamps peaked51, which is an interval observation rather than a claimed operation-body concurrency limit.

Recovery executed11 native cases: nine passed and two failed. Its initial inherited macOS /var symlink temp root was correctly rejected by storage. Only those two original failures were repeated with the original physical /private/tmp convention; both then reached native hosts and failed the strict peer-proof expected-detail assertion. Both original runs remain retained; neither is reported as a recovery pass.

The Unit failures are four existing QueueReadyAuthority first-refusal/recovery cases and two new checkpoint fixture seed cases. Focused native discovery succeeded with256 bound cases after correcting IPC execution permissions and physical/short pipe paths. The initial full census failure remains open. No performance hotspot was obtained from its interrupted diagnostic capture. Coverage and Linux/RF3/endurance acceptance remain open.
