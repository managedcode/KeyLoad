# Поточний протокол native session evidence для Website

Це джерельний контракт поточного intake, а не доказ успішної GitHub-кваліфікації. Єдиний producer — Benchmarks `comparison-aggregate`, артефакт `comparison-session-suite`; provider originals зберігаються в `comparison-session-provider-evidence`. Schema/profile — `session-v1`. Старі cohort, OpenLoop, composite, scenario-per-runner і repetition producers та їхні читачі вилучено. Історичні результати не конвертуються в поточні вимірювання.

## Канонічний склад і межі вимірювання

План містить рівно 22 фізичні measurement jobs: 11 баз × native 1/3 nodes, з окремою названою matrix для кожної бази. Кожен runner володіє лише однією базою та її реальною топологією. Окремі реальні TUnit tests користуються однією live database session. Початкове завантаження 1 000 000 записів вимірюється окремо; warmup, reset і п'ять repetitions відсутні. Двовузлові конфігурації не входять до активного плану.

Поточні сім case IDs: `load-million`, `index`, `sequential-read`, `random-read`, `search`, `mixed`, `verify-final`. Операційні lanes виконують по 100 000 операцій; mixed має create/delete/update/read/search з 16 клієнтами, розподіленими 4/3/3/3/3. Перший load має мільйон реальних create attempts. Original samples зберігають operation ordinal, worker, часові межі, success та safe error. Intake повторно обчислює counts і latency percentiles з оригіналів, перевіряє участь configured workers та часовий перетин mixed lanes. Участь усіх worker IDs сама по собі не доводить одночасну активність усіх клієнтів.

Ці сім document-session tests не доводять завершення всіх продуктових model families, vector accuracy, queue/event contracts чи загальної performance qualification. Native capability absence має явний unavailable результат без числових метрик; adapter/prerequisite gaps мають failure. Коли всі сім document cases є genuine unavailable для Qdrant/RabbitMQ/KurrentDB, envelope має `NativeSessionCapabilitiesUnavailable` і null observed profile, але зберігає actual native image та resource originals. Це не topology-unavailable. Native failure до перевірки профілю також може мати null profile лише за відсутності будь-якої measured case та з actual native-exit/qualification failure proof; partial failed samples зберігаються без публічних чисел. Непідтримувана native topology відрізняється від непідтримуваної операції. Для закритих Neo4j/SurrealDB/HelixDB n3 rows framework/cleanup nonzero exit може бути автентичним failure без server/resource/profile лише за наявності всіх семи original unavailable reports, native7Passed та actual notExecuted/nonzero-exit qualification receipt. Цей випадок не дозволяє missing TRX або fabricated cases. Corpus SHA256 та payload bytes зараз не вигадуються: projection залишає відповідні дані невідомими.

## Original authority та перевірка архівів

Native GitHub API captures прив'язують кожний original ZIP до repository/source/run/attempt/job/artifact ID і API digest. Усі 22 очікувані worker artifacts обов'язкові, включно з автентичними failed/null outcomes. Missing, corrupt, skipped, expired, foreign або mixed evidence відхиляється. Кількість original input files виводиться з конкретного authenticated aggregate; старих фіксованих totals немає. Локальна директорія сама по собі не автентифікує GitHub.

Worker `session.json` містить exact session/target/topology identity, actual native image refs, observed native DLL/PDB hashes, target profile/runtime і SHA256 references на сім original case reports, незмінений `NativeTestResults/<original>.trx`, `runtime-import.json`, `session-qualification.json` та, коли застосовно, `session-resource-evidence.json`. Original resource sidecar прив'язаний до original `session-state.json`. Native container DLL/PDB не порівнюються байт у байт з outer package: вони компілюються в іншому image build context і мають власну source/image-bound identity.

Preparation зберігає original immutable image proof, independent functional RF3 proof та `comparison-compiled-runtime-identity`: рівно два original JSON files, `benchmark-runtime.json` і `benchmark-runtime-files.json`. Inventory має bound 64 MiB. Descriptor та inventory hashes зв'язуються з кожним original worker import receipt. Workers завантажують повний `comparison-compiled-test-bundle` і перевіряють archive та кожний файл перед запуском. Collector і Website перевіряють малий identity ZIP та original metadata повного package, не завантажують 22 рази багатогігабайтні build outputs.

Збережені bounded native REST/rate-limit/header/image APIs. Collector перевіряє original API-digest-bound ZIPs повторно перед aggregate. Website використовує confined BCL ZIP preflight/extraction, перевіряє original worker/image/runtime/functional archives та exact extracted SHA256, після чого production projection ще раз читає original reports. Path escapes, symlinks, duplicate names, підміна hash/identity, перевищення bounds і змішані attempts не допускаються.

## Failure accounting та publication policy

Справжня окрема failed case має null metrics; раніші незалежні measured cases з valid native outcome, профілем і resource proof зберігаються для review. Глобальна post-qualification failure прибирає всі числа цієї сесії. Original qualification receipt містить actual native exit code і дві послідовні outer checks: `server-resource-cancellation` та `native-correctness`. Applicability фіксована: cancellation лише KeyLoad n1; correctness — KeyLoad/KurrentDB/PostgreSQL/Redis/OpenSearch/MongoDB/Neo4j. Решта мають явне notApplicable. Supported session з двома notApplicable не отримує вигаданих checks: її окремі native TUnit workload/oracle результати залишаються обов'язковими.

`validate` допускає повне автентичне failed/null accounting. `publish` допускає лише successful producer run, усі 22 successful worker jobs, passed native tests та лише measured/unavailable reports. Measured cases додатково вимагають qualified resources і actual verified native profile; failure/notQualified не оновлюють performance evidence. Ця умова діє і для незалежного Website запуску, а не лише для Benchmarks trigger. Permissions не розширюються.

Поточний finalizer відхиляє відсутні inner original reports. Окремий доказовий контракт failure до початку workload потребує native phase/marker ledger та outer failed TRX; його відсутність не замінюється fake cases або unavailable. Якщо відповідний контракт ще не приєднано до source, early startup failure лишається блокером повного aggregate.

Коли нового готового автентичного session producer немає, optional Website publish може збудувати content-only сайт без цифр. Уже обраний некоректний proof відхиляється; fallback на старий cohort чи вигадані metrics відсутній. Website/measured/trusted-control source revisions зберігаються окремо. Source configuration, build або контрольні parser fixtures не є performance measurements чи production readiness.

## Projection, browser та ресурсні величини

`isolated-projection.mjs` перевіряє original session bundle; `isolated-loader.mjs` перевіряє hash-bound catalog/projection та bounded native HTTP reads. Controls обирають case або окрему mixed lane, node count 1/3, database, metric і єдиний original session result. Median між repetitions і pooling між node counts відсутні. Mixed streams не усереднюються в штучний загальний latency distribution. Unavailable ніколи не перетворюється на numeric zero.

CPU, allocation і peak RSS — process observations за lane interval; overlapping lanes користуються одним процесом. Нормалізація CPU/allocation на кількість attempts вибраної lane не робить їх exclusive operation-family costs, і такі величини не можна підсумовувати. Server resource sidecar описує lifetime усієї session; intake не приписує його довільній окремій case.

`isolated-lab.mjs` володіє єдиною видимою benchmark surface, cancellation/generations, accessible controls, rows і disposal. `bootstrap.mjs` зберігає незалежний lazy scene lifecycle, reduced motion, fallback і joined cleanup. Official Three.js bytes, ліцензія та manifest не змінюються.

## TUnit, builder та native coverage

Усі тести — native TUnit. `SessionCohortContractTests` і `SiteSessionContractCoverageTests` виконують єдиний controlled `session-contract-probe.mjs`, що імпортує actual production modules. Дев'ять сценаріїв: plan, reports, selection, environment, qualification, runtime, site-context, publication, profile-accounting. Вони перевіряють positive inputs і конкретні identity/accounting/admission mutations; їхні значення ніколи не входять до published artifacts. Старий median-only NodeProbe видалено. Node children мають bounded concurrent stdout/stderr, deadline та joined ownership; Website children успадковують справжній `NODE_V8_COVERAGE`.

Незалежний C# oracle обчислює очікувані per-case/lane значення з authenticated originals. Реальні Chrome/CDP sessions завантажують builder-emitted assets через confined ephemeral HTTP listener і перевіряють controls, keyboard/accessibility, raw hash rejection, cancellation та stale generations. Scene regressions і native coverage converter regressions збережено.

Builder entry: `node site/scripts/build.mjs --isolated=<original aggregate directory> --output=<nonexistent isolated output> --revision=<measured SHA> --evidence-url=<exact producer run URL> --site-revision=<qualified website SHA>`. Duplicate/unknown/retired arguments, symlink/path escapes і змінені source/vendor bytes відхиляються. Original aggregate bytes зберігаються; browser отримує compact projection та source-bound static assets, а не raw samples. Чотири shared pure session modules копіюються без змін і перевіряються byte-for-byte. Failure видаляє тільки новостворений owned output.

Trusted-control source closure містить 65 поточних inputs: 61 authored JS source і 4 native PowerShell dependencies; native closure reader перевіряє exact sorted manifest і literal transitive imports, а browser roots перелічені явно. Це конфігурація source inventory, не твердження виконаного coverage. Кожний required authored JS без actual native execution ranges лишається uncovered. Thresholds не знижено: 80% lines, 70% branches, 90% для critical validators.

GitHub задає absolute `KEYLOAD_SITE_COVERAGE`, original Node V8 receipts та реальні Chrome precise-coverage receipts. Immutable source manifest прив'язаний до actual Website checkout HEAD; source identity сама по собі не є execution proof. Browser URLs `/Features/BenchmarkComparisons/...mjs` і `/scripts/Features/BenchmarkComparisons/...mjs` відображаються на exact repository sources лише з allowlisted origins. Native ranges, stdout та artifact identities зберігаються; ranges не фабрикуються.

Deploy можливий лише після Aspire-owned TUnit/analyzer/browser/coverage qualification та exact source/producer freshness через needs-gated least-privilege Pages job. Benchmark і Website qualification виконуються лише Linux GitHub Actions. Цей source rewrite ще не є доказом успішних Linux gates.

## Native range union repair — TASK-SITE-013

Preserve each native receipt and script as a separate execution snapshot: raw receipt identity/hash, scriptId, optional native execution context, and that script's own functions/ranges. Resolve the deepest physical function and block for a source offset inside one snapshot before taking the covered union across snapshots. Flattening ranges across snapshots is forbidden: a zero-count child in one execution must not override a positive parent in another execution where V8 legitimately omitted that same-count child.

Physical function identity is the exact source path and outer function span; functionName remains metadata and must not create duplicate denominators. The outcome denominator is the union of observed native child intervals keyed by source, physical function span and child span. A matching positive explicit outcome proves coverage. When the interval is omitted in another snapshot of the same physical function, infer coverage only when that snapshot proves positive effective execution throughout the canonical interval. An enclosing module is insufficient proof. A positive explicit outer outcome is not invalidated by an unexecuted descendant. Preserve native receipts unchanged and record derived decisions separately.

Regression proof uses actual Node-generated receipts from both sides of a real branch. Assert exact executable/covered totals, the true and false return lines covered, an uncalled function uncovered, and identical results after receipt-order reversal. Separate/nested function identity proof also uses real Node output. Malformed/crossing-range cases mutate only the fixture's exact file-URL script, never an unrelated built-in script. Every child process starts bounded concurrent stdout/stderr drains, has a deadline, and joins or kills/joins the owned process in finally. Local development runs remain development evidence; fabricated native coverage never qualifies publication.

### Native equal-span function contract

The [V8 native coverage implementation](https://raw.githubusercontent.com/v8/v8/main/src/debug/debug-coverage.cc)
explicitly permits a script wrapper and uncalled inner function with identical
spans; native report order is nesting order and the inner function is last.
TASK013 must preserve that order and resolve equal-span ties to the native
innermost function within each snapshot. Apply this selection to line resolution,
explicit outcome checks and omitted-child inference, so script execution cannot
prove an uncalled inner function executed. Names remain metadata and span-based
branch denominators stay unchanged. A real Node single-function source with no
trailing newline proves native equal spans/order and uncovered function state;
retain the existing Unicode/CRLF9/8line2/2branch and reversed-receipt regressions.
Only source review/build and exact-SHA GitHub execution can join this correction.

Native converter regression evidence must survive temporary-directory teardown.
TASK013 retains unchanged real fixture source, original Node receipts, stdout and
raw SHA256/identity metadata under a separate converter-fixtures subtree of
KEYLOAD_SITE_COVERAGE. These files are uploaded for review and never enter the
production node/ or browser/sessions/ coverage denominators. Preserve original
native bytes and cleanup of owned temporary files; mutated malformed/crossing
copies must not be labeled or retained as actual executed native evidence. Root
reviews the retained packet and actual GitHub converter assertions before join.
