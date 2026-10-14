# Orleans Messaging feature

## Purpose and entry points
- Owns the Orleans due-work grain service, atomic-partition coordinator grain, and their typed internal contracts.
- Entry points: `GrainServices/RecurringDueGrainService.cs` and `Grains/RecurringDueCoordinatorGrain.cs`.

## Boundaries
- The per-silo service only discovers hints after current leadership and a quorum read barrier. The keyed coordinator sends work through a fresh signed `IRequestGrain` request and drains the existing native Communication CQRS stream.
- Never call `DatabaseEngine.Apply` or `ICommitCoordinator.SubmitNativeAsync` from this feature. Do not create a scheduler principal, trust roles in hints, or move node-local storage handles with activations.
- Preserve full partition identity, persisted creator revalidation, one-grain-per-request isolation, one five-second dispatch deadline, and stable command identity across the single uncertainty retry.

## Verification and risks
- Applicable skill: owner-authorized Orleans skill at `/Users/ksemenenko/.codex/skills/orleans/SKILL.md`.
- Author focused TUnit tests and let the solution integrator run all build and Aspire gates. Native local unit/runtime checks do not qualify RF3.
- Grain-service startup, stop and task joining are lifecycle-sensitive. Keep the per-silo loop bounded, sequential and cancellation-linked; never detach active dispatch work.

## Owner-approved KL092 queue coordination
- QueueDeadlinePrincipalId selects only an operator-created EXISTING persisted principal. Never create/bootstrap/impersonate a scheduler principal. Revalidate scoped QueueConsume and current worker-input authority for every dispatch/effect/replay. Null is unavailable.
- ADR-125 connection ownership supersedes the historical IRequestGrain activation wording: call-local operations use the actual existing ConnectionGrain and fresh per-operation context; no extra activation or dispatcher. Preserve the original five-second deadline, one uncertainty retry and joined shutdown.


## Finite native Accept capacity coordination, TASK-KL094-ACCEPT-CAPACITY-ATTEMPT-002
- ADR-088/094/125 owns the optional signed failure witness and separately authorized source CAS. Discovery remains read-only; it cannot advance attempts or grant authority. Every effect remains the original native signed Batch on the stable ConnectionGrain call-local owner, with fresh persisted subject permissions, original cancellation/deadline and joined CQRS tasks. No per-operation activation, fallback principal, new dispatcher or DurableJobs retry authority.
- Nullable source ceiling/history and target original failure stamp are charged under actual native count/byte/serialization limits. Unknown or unrelated dependency changes do not advance; known source failure requires explicit existing operator repair. This finite capacity-only boundary does not close universal AC-XFER-005.
