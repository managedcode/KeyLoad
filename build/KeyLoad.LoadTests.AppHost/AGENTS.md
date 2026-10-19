# KeyLoad load-test infrastructure

## Purpose, entry points and ownership

Own exactly the infrastructure for one selected database and topology.
`Program.cs` visibly composes, builds, runs and disposes this Aspire application.
The ordinary development and functional-test host is `../KeyLoad.AppHost`.
Do not introduce another AppHost, resource runner or forwarding project.

External databases use the selected pinned images and their entry points.
KeyLoad uses execution `AddProject<KeyLoad>` processes and required Azurite
membership storage. Ordinary build, startup, functional tests and load execution
never publish or start a KeyLoad server or workload image. Execution .NET SDK
`PublishContainer` belongs only to explicit release/publication.

Aspire exposes actual selected endpoints, readiness and completion barriers to the
separate workload client. The client owns corpus loading, measured
operations, result verification and its actual process/output-reader lifetime.
Do not add that workload to the resource graph or run database work in AppHost.
Use the installed Aspire and Orleans skills for their respective boundaries.

## Boundaries and protected risks

Each selected database/scenario has a fresh owned lifecycle. Containers use session
lifetime and writable anonymous volumes; image-user defaults own
their storage. Retain readonly bootstrap/configuration mounts where required.
On completion or failure, join the original client and observers, dispose the
actual Aspire application, and remove only run-owned resources. Preserve unrelated
Docker resources and original primary/cleanup failures. An abrupt process fault
uses the AppHost-owned PID plus Aspire/OS start identity command and joined actual
exit; graceful stop is not abrupt-fault evidence.

Capture actual source/DLL/PDB/process identities without substituting image fields.
Local execution has no invented GitHub provenance and cannot authorize public
figures. Publication independently requires authenticated original GitHub
source/run/attempt/job artifacts, matched resources and complete workload/result
oracles. process resource equivalence and external advertised-endpoint
reachability require real qualification; never weaken gates to describe them as
complete. Keep current qualification limits explicit.

Follow the root policy for independent silo/copy configuration, storage safety,
authorization, package ownership, versions, numeric gates and shared-checkout work.
Do not implement a database runtime, membership algorithm, scheduler, serializer
substitute or second container lifecycle here.

## Commands and verification

From the repository root:

```sh
dotnet build build/KeyLoad.LoadTests.AppHost/KeyLoad.LoadTests.AppHost.csproj --no-restore -warnaserror
```

The lead coordinates execution windows. Every build uses `-warnaserror`.
Formatting, strict complete-solution build, TUnit lifecycle/cancellation
checks and real selected-database execution are distinct gates. Source inspection
or a graph model alone proves no runtime, performance or publication qualification.

## Skills

Use the installed Aspire skill for resource composition and lifecycle. Apply the
installed Orleans skill for Orleans decisions and the relevant formatting or
diagnostics skill only within the authorized verification window. No additional
library, container framework or package version is authorized by this policy.
