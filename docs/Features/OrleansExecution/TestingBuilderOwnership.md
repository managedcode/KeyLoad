# Testing builder ownership before application transfer

This OrleansExecution test-owner contract covers the Aspire-owned cluster test wave used by EventStreams, Messaging, StorageRecovery and other replicated-cluster scenarios. [OrleansExecution](../OrleansExecution.md), [ADR117](../../ADR/ADR-117-tunit-ci-entry.md) and [ADR125](../../ADR/ADR-125-connection-owned-execution.md) retain real TUnit fixtures, Aspire resources and connection-owned execution. It adds no product operation, provider, SDK, readiness substitute or test case. Frontend and benchmarks are N/A.

## Actual contract

The centrally pinned Aspire.Hosting.Testing 13.6.1 package identifies repository commit 3751e615ad660621f2a7aa4c390ef48e08a0eed8. Its actual generic [testing builder](https://github.com/microsoft/aspire/blob/3751e615ad660621f2a7aa4c390ef48e08a0eed8/src/Aspire.Hosting.Testing/DistributedApplicationTestingBuilder.cs) returns a disposable builder over a suspended factory. Create failure awaits its factory disposal before returning an error; the unsuccessful-application branch does not await entrypoint exit and therefore does not establish join. A successful Build returns an application using the same factory; disposing the builder after transfer would dispose that application too.

Before this correction, StartCore retained the builder only in a local variable and Wave owned only the application assigned after BuildAsync. Failure after Create returned, or cancellation of the Build wait, could leave no KeyLoad path requesting actual builder disposal. This is a confirmed source ownership omission; no process leak is claimed from source inspection.

The pinned [factory](https://github.com/microsoft/aspire/blob/3751e615ad660621f2a7aa4c390ef48e08a0eed8/src/Aspire.Hosting.Testing/DistributedApplicationFactory.cs) cancels its state and can return from Dispose before an application has completed successfully. Its successful-application branch also uses bounded shutdown waits. The [entrypoint invoker](https://github.com/microsoft/aspire/blob/3751e615ad660621f2a7aa4c390ef48e08a0eed8/src/Aspire.Hosting.Testing/DistributedApplicationEntryPointInvoker.cs) owns an actual background thread and diagnostic subscription. Awaiting the actual Dispose task is required; its completion is not proof that every underlying thread, listener or process joined. Caller Build cancellation likewise proves only that caller wait ended.

## Requirements and acceptance

- REQ-CLUSTER-BUILDER-001 / AC-CLUSTER-BUILDER-001: begin the current generation before original startup work. Reset applicationDisposed, nodeLocksReleased and startAttempted so earlier successful shutdown flags cannot authorize current evidence deletion. Refuse overlapping application ownership or restart after unresolved pretransfer ownership. Keep root retention and cleanup-failure state sticky.
- REQ-CLUSTER-BUILDER-002 / AC-CLUSTER-BUILDER-002: immediately register the exact builder returned by the unchanged generic CreateAsync before any callback or await. Use the original arguments, settings, token, stage order, image admission/preparation owner, enrollments, parameters/endpoints, readiness and deadlines. A failed Create has no returned builder to invent or dispose at the callsite.
- REQ-CLUSTER-BUILDER-003 / AC-CLUSTER-BUILDER-003: transfer ownership synchronously immediately after assigning the actual BuildAsync result to Wave. No callback, await or validation separates assignment and transfer. Never dispose the builder after application ownership transfers; preserve existing application Stop/Dispose, callback Join and node-owner cleanup.
- REQ-CLUSTER-BUILDER-004 / AC-CLUSTER-BUILDER-004: record the original initiating startup exception before retention work. Preserve that exact exception for the existing Failed callback even if RetainRoots fails and the wrapper throws an aggregate. Evaluate the cancellation boolean at the original callback boundary. Keep initiating, retention, callback and actual cleanup errors in the ordered ServerFailureObserver ledgers; do not replace or suppress them.
- REQ-CLUSTER-BUILDER-005 / AC-CLUSTER-BUILDER-005: request pretransfer DisposeAsync through the common DisposeApplicationAsync boundary used by initial and restart callers. Memoize its original Task, including synchronous invocation failure, and observe that same task on subsequent cleanup. Do not substitute a later no-op Dispose, detach work or enlarge deadlines. Preserve original admission/isolation/application/lock/control/image cleanup order.
- REQ-CLUSTER-BUILDER-006 / AC-CLUSTER-BUILDER-006: retain failed startup roots and control evidence before cleanup. A lifetime retention veto is sticky, including after successful Dispose. Never set applicationDisposed or nodeLocksReleased from builder disposal, and never use stale prior-generation flags to delete adjunct evidence. pretransfer joinedness stays unresolved; no fabricated cleanup exception is added merely because source lacks join proof.

```mermaid
flowchart TD
    B[Begin current generation and reset joined flags] --> C[Original CreateAsync]
    C -->|Returns builder| R[Register exact builder immediately]
    C -->|Fails| F[Record original startup failure and retain evidence]
    R --> S[Original prebuild stages]
    S --> A[Original BuildAsync]
    S -->|Failure| F
    A -->|Failure or caller cancellation| F
    A -->|Returns actual application| T[Assign application then transfer synchronously]
    T --> P[Original start and readiness]
    P -->|Failure| F
    F --> K[Original Failed callback sees initiating exception]
    K --> D[Common owner cleanup]
    D -->|Pretransfer builder owned| N[Observe same actual Dispose task and retain roots]
    D -->|Application owns factory| J[Original application and node owner cleanup]
    N --> O[pretransfer join qualification remains open]
```

## Ownership and qualification

The wave's builder-lifetime helper (`TwoClusterMembershipBuilderLifetime` in `tests/KeyLoad.ClusterTests/OrleansExecution/Lifecycle/`) owns only acquisition, current-generation reset, failure retention, transfer and the original disposal task. The wave contains the field and the common cleanup/root-veto hooks. The existing application-shutdown helper keeps its application Stop/deadline/Dispose sequence; the wave keeps its flag bookkeeping and callback Join after that sequence. Startup keeps its ordered stages and Failed callback before real owner cleanup. Direct start owners still settle their owned wave after failure.

`ClusterStartupEvidenceTests` covers a different in-process cluster owner, and `AspireFailureStartupTests` covers BeforeStart on an already built application; neither substitutes for this gap. The existing Aspire replicated-cluster scenarios that start this wave remain the regression coverage.

AC-CLUSTER-BUILDER-001..006 carry an explicit evidence exception: no exact pretransfer failure/cancellation regression with full closure observability exists. Source inspection proves retention before Register even when Create fails, but supplies no runtime join witness. No fake builder, mock provider, synthetic readiness or artificial Build/Run is accepted as closure. This correction requests the required disposal and conservatively retains uncertain ownership evidence; it does not qualify full shutdown.

The wave's current six-silo, two-group membership topology is a forbidden self-written cluster fixture and is source debt; see `sourceDebt` in [status.json](../../implementation/status.json). The builder-ownership rules above apply unchanged to the replacement Aspire topology.
