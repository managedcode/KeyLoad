# GrainServiceGraph within ClusterRouting

Accepted dependency-repair contract for ADR-094 and REQ/AC-DUE-002/004.
ManagedCode.Orleans.Graph10.0.9 admits only IGrain sources in its fluent and
attribute configuration. The native due service is an IGrainService system
target. Giving its coordinator client entry or fabricating caller context would
weaken the intended service-to-coordinator boundary.

| Requirement | Acceptance |
|---|---|
| REQ-SGRAPH-001: native service-source policy | AC-SGRAPH-001: the owning Graph package exposes AddGrainServiceTransition<TService,TGrain>(params string[] targetMethods), constrained to the actual Orleans GrainService implementation and IGrain target. It adds only the actual service type, explicitly any native callback source method, and the supplied target methods. Empty/null/blank input fails before changing the existing graph; all previous method rules remain effective. |
| REQ-SGRAPH-002: real caller enforcement | AC-SGRAPH-002: a native Orleans-hosted service callback outside a client request reaches the explicitly permitted application-grain method, a different target method is rejected, and direct client entry remains denied. The observed source is the actual service implementation, never CLIENT, UNKNOWN, the base Grain, a fabricated context or a reflection-order guess. Existing application-grain, system-target and async-stream regressions pass. |
| REQ-SGRAPH-003: published ownership and consumer join | AC-SGRAPH-003: repair source and focused regressions live in the sibling Orleans.Graph repository; its canonical patch release passes required checks and GitHub publication and is available from the real NuGet feed. Only then update KeyLoad's central package reference and use the native API for RecurringDueGrainService to IRecurringDueCoordinatorGrain.ProcessDueAsync plus the existing coordinator-to-request and request-to-leaf edges. Actual Aspire SDK/MCP autonomous RF3 and complete required Linux gates remain mandatory. |

The new Graph method is an additive startup configuration API. No KeyLoad public
operation, native replica/request wire contract, serialization alias, persisted
format or topology changes. The explicit any-source-method rule represents native
service callbacks without a current incoming method; it does not infer an exact
method or allow another source type, target method or client. Existing runtime
caller resolution is preserved unless the real hosted regression demonstrates a
defect, which must be repaired and tested in the owning Graph repository.

Root owns planning, final review, actual dependency delivery, central package and
Server DI/Graph joins. Luna cluster_wave prepares a private Graph patch for the
two existing builder files and new real hosted/builder regressions. It first
records this contract and an owning-repository ADR, then implements the additive
method and independent allowed/denied/client and additive-rule tests. No consumer
copy, local-package release claim, project reference, AllowAll, client entry,
filter bypass, guessed caller or context impersonation is permitted.

Build, formatter, TUnit, commit/push, successful Release and verified package
availability are ordered owning-repository gates; root runs/reviews them and
retains exact source/run/package receipts. KeyLoad source integration follows
publication. Rollback retains the existing package and leaves autonomous due
coordination unregistered until a capable published package is available. Source
presence or a local build does not close any of the three acceptance criteria.

Frontend and new SDK/MCP syntax N/A: this is native internal routing configuration.
Related original task is KL-100; it remains open through complete S2 qualification.
