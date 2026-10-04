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
