# node resource ownership

Lifecycle ownership of one silo's local resources under [ADR-136](../../ADR/ADR-136-orleans-partition-ownership.md). Parent: [OrleansExecution](../OrleansExecution.md).

REQ-EXECUTION-NODE-RESOURCES-001: the inert application node retains its original lifecycle lock, preparation, initialization, bootstrap and shutdown tasks. Only initialization borrows the application-owned storage host (the silo-local ZoneTree owner). Neither resource owner resolves storage or services during construction.

REQ-EXECUTION-NODE-RESOURCES-002: the request lifetime owns the request registry and both linked cancellation sources. The local runtime owns acquired documents, graphs and models, and captures receivers immediately. A partial acquisition remains reachable before the next await or cancellation check.

REQ-EXECUTION-NODE-RESOURCES-003: shutdown retains the original ordered startup and cleanup failure vectors. The order registry, then node, then producers, then storage services, then the storage owner's close permit is mandatory. Request work reporting joined does not erase a cancellation callback failure.

AC-EXECUTION-NODE-RESOURCES-001: the preparation and bootstrap tasks settle after both cancellation sources are cancelled. Drain starts before administration disposal; graph, model and blob shutdown join before documents; documents join before the known-owner predicate.

AC-EXECUTION-NODE-RESOURCES-002: only the joined known-owner predicate permits request-registry and linked-source disposal. Only an empty cleanup vector publishes the terminal closure permit. Node disposal may rejoin the same memoized owner disposal tasks after that permit; it cannot start another close or release unknown owners.

```mermaid
flowchart TD
    Execution[initialization] --> Borrow[Borrow silo-local storage owner]
    Borrow --> Local[Publish acquired local runtime]
    Local --> Active[Active publication]
    Active --> Bootstrap[Database bootstrap]
    Registry[Connection registry shutdown] --> Cancel[Cancel and join startup]
    Cancel --> Host[Request drain then admin and observers]
    Host --> Runtime[Graphs models blobs then documents]
    Runtime --> Known[Joined known-owner predicate]
    Known --> Lifetime[Dispose registry and linked sources]
    Lifetime --> Permit[Empty cleanup vector publishes closure]
```

The storage host is not registered as a DI-owned borrowed result. The outer application storage owner alone performs the actual storage disposal after every producer permit. Orleans membership and activation migration never move node-local open handles; replication to ReplicaCount silos happens through ordinary Orleans calls to each silo's ZoneTree owner.
