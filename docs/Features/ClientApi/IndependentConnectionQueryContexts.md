# Independent authorized query contexts on the actual connection owner

Architecture requires one blocked query not to block all requests of its principal, idle owners not to retain index handles, and interleaving not to alter security context. Under ADR125 the actual physical Kestrel connection owns one bounded ConnectionGrain; every operation keeps its unique signed request ID, caller-local preparation/budget/token, current persisted principal and node-local storage ownership. This operation does not add a SessionGrain or claim a product defect from missing tests.

Existing AcCrs051 and public AcCrs058 hold COMMAND while a differently authorized command/document completes. The new whole ConnectionQueryContextClusterTests operation covers the separate actual public SQL Query and JSON/C# AstQuery branches. It reuses the genuine ConnectionClusterScenario private-probe Aspire cluster, persisted two nonadmin identities in separate scopes, SocketsHttpHandler one physical HTTP2 connection and original deadline/task/probe/producer-settlement owners. New independent literal second documents and complete QPD receipts are retained and replayed; the original Scenario baseline document remains unchanged.

Hold first subject SQL at actual signed RequestStarted; all second-subject SQL/JSON/C# first and interchangeable continuation pages complete through SAME witnessed connection/grain/activation while the original task remains pending. Cancel only the original linked read token, require Cancelled/null result and independently join original producer disposal/arm retirement before throwing any classification failure. Known-read cancellation never becomes an unknown write. Every response independently conserves four full source documents, complete Outbox and both original new command receipt replays even after primary failure. Public positions/cursors remain opaque; initial position is positive and the partition scope is frozen before commit, never predicted from a counter.

Same original credential/owner observes foreign-principal cursor refusal, Query-grant epoch3 denial, independent second-principal health, epoch4 repair and old first cursor invalidation followed by fresh literal pages. Explicit close removes the witnessed activation; reconnect through a genuinely new HTTP2 transport has different connection/grain/activation and correct scope for both credentials. Official MCP keeps its ACTUAL HTTP1 sequential profile: second-subject SDK then first-subject official SQL/JSON/C# calls share the same witnessed physical owner, so principal context cannot be borrowed. Epoch5 Query denial and epoch6 repair/stale cursor checks use the actual official safe error envelope, original operation/disposal joins and full conservation, then explicit close. No MCP multiplex capability is invented.

Original RuntimeConnectionOperation/Failure/Settlement/CloseAdmission and ConnectionClusterSequential/Overlap/Authorization whole classes remain mandatory. No production scheduling, trust, limits, aliases, IDs, package, parser, query hash or read-cut contract changes. ConnectionClusterScenario currently exposes no same-data cold operation; genuine two-cold session recovery and direct idle index-handle/lease measurement remain explicit unresolved whole-task gates, not manufactured API calls or getter-based resource proofs.

Before EACH cold restart of the Aspire-owned nodes, join all original tasks/producers, dispose all original clients and the administrator, and fail closed if ownership has not settled. Restarts go through the AppHost-owned resource commands; partial failures are retained and every stopped node is started before health waits. Preserve all original receipts across cycles, reopen the real administrator, then run full SDK/official pages/conservation/receipt replay. Both cold attempts settle into one retained failure ledger before the aggregate throw. No detached child, reset deadline, trusted role or power-loss claim.

```mermaid
flowchart LR
    Connection[Actual connection owner] --> First[Held query context]
    Connection --> Other[Independent SQL AST and C# contexts]
    First --> Cancel[Cancel and join original producer]
    Other --> Rows[Complete independent results]
    Cancel --> Close[Close callers before cold restart]
    Rows --> Close
```
