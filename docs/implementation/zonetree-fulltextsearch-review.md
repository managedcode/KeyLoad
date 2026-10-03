# ZoneTree.FullTextSearch candidate review

Owner suggestion2026-10-03 is included in the active Search/SQL workstream.
Decision remains evaluation under ADR-009/065, REQ-SQLC-010 / AC-SQLC-010.
No package is installed and no production provider integration is implemented.

Official source inspected at commit `6d7710a4845608a5ab266342c3e93808f53a8695`:
[engine](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/src/ZoneTree.FullTextSearch/SearchEngines/HashedSearchEngine.cs),
[index](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/src/ZoneTree.FullTextSearch/Index/IndexOfTokenRecordPreviousToken.cs),
[tokenizer](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/src/ZoneTree.FullTextSearch/Tokenizer/WordTokenizer.cs),
[hash](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/src/ZoneTree.FullTextSearch/Hashing/DefaultHashCodeGenerator.cs),
[MIT license](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/LICENSE).
NuGet metadata inspected:1.0.9, net8/9/10, ZoneTree>=1.8.7; KeyLoad pins1.9.8.
Source commit/package provenance equivalence has not been verified. Upstream
performance figures are not KeyLoad comparative evidence.

The library supplies add/update/delete, boolean/facet/ordered-token queries and
paging over its own ZoneTree postings. The inspected index uses primary and
optional secondary trees with independent sequential writes and maintainers;
there is no demonstrated join to KeyLoad's canonical atomic commit/WAL. Hash-only
token keys, default short-token/digit/Unicode behavior, unordered result sets and
partial results on cancellation differ from KeyLoad's exact BM25/RRF/scoped-cut
contract. These are integration obligations, not evidence that the library is
unsuitable in every role. Its query grammar does not replace the SQL language.

Recommended role to investigate: disposable versioned candidate projection over
committed canonical data. Eligibility must bind partition/source position/schema/
tokenizer/hash generation and prove completeness for the actual authorized cut.
Canonical documents, authority, exact corpus statistics, final scoring/fusion and
projection remain KeyLoad-owned. Missing/stale/corrupt/partial generation cannot
produce successful partial results. Freeze atomic publication, rebuild, budgets,
cleanup, rollback and every authoritative/freshness join before implementation.

## Required matched tests before integration or acceleration claims

|Gate|Pass condition and evidence|
|---|---|
|Token/identity/rank parity|Real ZoneTree exact-oracle corpus covers NFKC, Rune/supplementary letters, digits, short/repeated/empty terms, deliberate hash collisions, exact identities/scores/ties and full branch ranks before RRF. No mock provider.|
|Mutation/recovery|Real insert/update/delete/revision and interruption during posting/publication; restart/rebuild each actual replica from committed source. Incomplete generations never serve success; full business oracle at the validated cut.|
|Privacy/cuts|Persisted row/field policies, tenant/partition boundaries, stale/deleted/hidden rows and concurrent writes preserve the same authorized source cut.|
|Resources/cancellation|Bounded tokens/candidates/bytes/cache/build concurrency and temporary disk; cancellation fails rather than returns partial success, and following requests recover. Track foreground write amplification and index lag.|
|Native public flow|Docker/Aspire RF3 using actual .NET SDK and official MCP, restart/failover/migration and missing/current/rebuilding index states. Tests only in GitHub.|
|Performance|One isolated Linux agent per actual1/2/3node/scenario, matched read/create/update/delete oracle/ACK/corpus/concurrency. Retain source/run/attempt JSON plus p50/p95/p99, throughput, CPU/allocations/RSS/disk, rebuild cost and error/lag; complete authenticated cohort alone may refresh site metrics.|

Research task TASK-SQLC-R4 completed source review; raw packet SHA256
`094be38e3e22063f95bddef591ae150721285975129cff51463580975e2ea55b`.
Integration, runtime correctness, native recovery and performance remain pending.
The packet's immutable upstream links are durable references; temporary local
analysis is not provider qualification or a substitute for uploaded CI artifacts.
