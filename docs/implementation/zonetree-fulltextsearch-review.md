# ZoneTree.FullTextSearch source review

Owner selection2026-10-03 fixes the provider under
[ADR-071](../ADR/ADR-071-canonical-zonetree-providers.md); the active Search/SQL
workstream retains REQ-SQLC-010 / AC-SQLC-010 and REQ/AC-ZT-003.
The selected package is centrally pinned and the native derived projection is implemented in Server/Features/Search. Source presence is not complete runtime qualification.
The owner's provider decision is separate from this source inspection and does
not establish runtime integration or performance qualification.

Official source inspected at commit `6d7710a4845608a5ab266342c3e93808f53a8695`:
[engine](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/src/ZoneTree.FullTextSearch/SearchEngines/HashedSearchEngine.cs),
[index](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/src/ZoneTree.FullTextSearch/Index/IndexOfTokenRecordPreviousToken.cs),
[tokenizer](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/src/ZoneTree.FullTextSearch/Tokenizer/WordTokenizer.cs),
[hash](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/src/ZoneTree.FullTextSearch/Hashing/DefaultHashCodeGenerator.cs),
[MIT license](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/6d7710a4845608a5ab266342c3e93808f53a8695/LICENSE).
NuGet metadata inspected:1.0.9, net8/9/10, ZoneTree>=1.8.7; KeyLoad pins1.9.8.
The actual published 1.0.9 package was downloaded and inspected on 2026-10-03.
Its SHA256 is `7ea1fbb7aba0ad00391d78d2f414166b500335f5b1affe43c305d861b55719ff`.
The nuspec repository commit and DLL informational version identify
`c0993e2c65186708f3b087741082545bf49224fd`, the exact release source inspected
alongside these bytes. The package includes its MIT license and net10 assembly,
with ZoneTree >=1.8.7; it names the former koculu repository URL. The earlier
review commit `6d7710a4845608a5ab266342c3e93808f53a8695` is a later descendant
repository-move merge, not the published package commit. No PDB was included and
the corresponding public snupkg returned HTTP 404. NuGet signature verification
on this macOS host failed with CSSM_ModuleLoad/NU3003, so signature qualification
remains a separate Linux gate. Upstream performance figures are not KeyLoad
comparative evidence.

The library supplies add/update/delete, boolean/facet/ordered-token queries and
paging over its own ZoneTree postings. The inspected index uses primary and
optional secondary trees with independent sequential writes and maintainers;
there is no demonstrated join to KeyLoad's canonical atomic commit/WAL. Hash-only
token keys, default short-token/digit/Unicode behavior, unordered result sets and
partial results on cancellation differ from KeyLoad's exact BM25/RRF/scoped-cut
contract. These are integration obligations, not evidence that the library is
unsuitable in every role. Its query grammar does not replace the SQL language.

Implemented source role: disposable versioned candidate projection over
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
|Performance|One isolated Linux agent per actual1/2/3node/scenario, matched read/create/update/delete oracle/ACK/corpus/concurrency. Validate actual100k/1m persisted records and at least100k measured operations per applicable cell; include deterministic random/sequential, ordered/range/index and complex-query flows. Retain source/run/attempt JSON plus p50/p95/p99, throughput, CPU/allocations/RSS/disk, rebuild cost and error/lag; complete authenticated cohort alone may refresh site metrics.|

Research task TASK-SQLC-R4 completed source review; raw packet SHA256
`094be38e3e22063f95bddef591ae150721285975129cff51463580975e2ea55b`.
Integration, runtime correctness, native recovery and performance remain pending.
The packet's immutable upstream links are durable references; temporary local
analysis is not provider qualification or a substitute for uploaded CI artifacts.


## KL028 current API/license audit, 2026-10-09

The existing cached1.0.9 package SHA above was independently rechecked against its actual bytes. Its nuspec release commit is c0993e2c65186708f3b087741082545bf49224fd and its included license is MIT; the signature entry exists, but signature validation remains an actual Linux gate. No feed-availability or publication inference follows from the local cache. The earlier macOS NU3003 and snupkg404 are preserved historical observations. Only100k/1m are active scales; any historical5m evidence retains its original scope.

|Audit surface|Primary API evidence|Selected-provider conclusion/test ownership|
|---|---|---|
|Native posting/rank|[Exact1.0.9 IndexOfTokenRecordPreviousToken](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/c0993e2c65186708f3b087741082545bf49224fd/src/ZoneTree.FullTextSearch/Index/IndexOfTokenRecordPreviousToken.cs) exposes native primary posting upsert/delete, optional secondary tree and TRecord[] search|NativeTextIndex uses primary token/record/previous-token tuples and synchronous native WAL. Canonical scoring, authorization and cut validation remain KeyLoad-owned; parity/collision and literal bilingual tests validate this composition.|
|Cancellation|[Exact release search implementation](https://github.com/ZoneTree/ZoneTree.FullTextSearch/blob/c0993e2c65186708f3b087741082545bf49224fd/src/ZoneTree.FullTextSearch/Search/SearchOnIndexOfTokenRecordPreviousToken.cs) returns accumulated records when cancellation is observed|KeyLoad uses its bounded native iterator path and fails cancelled work; NativeTextAsyncAdmission/Lease/Settlement cases prove genuine cancellation/no partial success/healthy settlement. No native partial-return search is adopted as successful KeyLoad output.|
|Lucene BM25 evaluation|[Apache Lucene.NET4.8.0-beta00017 BM25Similarity](https://lucenenet.apache.org/docs/4.8.0-beta00017/api/core/Lucene.Net.Search.Similarities.BM25Similarity.html) exposes scoring with default k1=1.2,b=0.75, corpus statistics and overlap handling|Primary API capability only, not comparative performance or an integrated KeyLoad provider. Existing selected native postings do not supply those scores.|
|Lucene analyzer evaluation|[EnglishAnalyzer](https://lucenenet.apache.org/docs/4.8.0-beta00017/api/analysis-common/Lucene.Net.Analysis.En.EnglishAnalyzer.html) defines English stopword/stemming composition|No Ukrainian stemming, cross-analyzer equivalence or KeyLoad selectable analyzer claim; current literal bilingual contract uses the frozen KeyLoad tokenizer.|
|Lucene format/license evaluation|[Lucene46Codec](https://lucenenet.apache.org/docs/4.8.0-beta00017/api/core/Lucene.Net.Codecs.Lucene46.Lucene46Codec.html), [official Apache license/notices](https://github.com/apache/lucenenet/blob/Lucene.Net_4_8_0_beta00017/LICENSE.txt)|Distinct Lucene index formats and Apache2 license; no format compatibility, migration, dependency addition or alternative-provider selection. Selected FTS package retains its MIT license.|
|Replay/delete/formats|Actual canonical DeleteDocument commit and exact receipt replay→native/canonical reopen, plus real corrupt-manifest rejection→exact fixture repair→native cold healthy continuation|NativeTextBilingualLifecycleTests and NativeTextProjectionRestartTests. Disposable projection writes do not replace atomic canonical/replication journals.|

Original9c/run37920436876 task archives authenticate matching original native source/PDB before/after, typed census/TRX unions and joined processes. Selected provider supporting parity/bilingual/authority/bounds/restart cases passed in both original profiles; the complete KL029 task nevertheless failed (unit57/58, recovery18/18, RF33/9 each). Its leader-loss public result failed, so this audit cannot promote that original RF3 or newer source. Complete same-source Linux suites, native signature, resources/fault/coverage and matched measurements remain required. New manifest continuation has no native execution evidence yet.
