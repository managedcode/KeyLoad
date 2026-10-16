# Діагностика Benchmarks — 2026-10-16

Стан доказів: read-only GitHub Actions receipts, зібрані 2026-10-16 приблизно о
20:38–20:45 UTC. Локальні benchmark builds, checks, tests і workloads не запускалися.
Це діагностика попередніх запусків, а не підтвердження виправлення чи вимірювання
продуктивності нового коду. Час у документі — UTC.

## Підтверджений результат

Довгий червоний pipeline не означає довгий виміряний workload. У запуску
[38811914189](https://github.com/managedcode/KeyLoad/actions/runs/38811914189)
було 1 898 matrix workers; кожний `Run database workload` завершився failure за
0–5 секунд (API timestamps мають секундну точність). Повторна підготовка контейнерів, повторні збірки й очікування runner
розтягнули невдалу матрицю на 12 год 33 хв. Останній завершений запуск
[38955544811](https://github.com/managedcode/KeyLoad/actions/runs/38955544811)
взагалі зупинився на analyzer-enabled build, до workload.

Останні 100 запусків Benchmarks у bounded вибірці від 2026-10-09 20:21:15 до
2026-10-16 20:17:27: **48 failure, 51 cancelled, 1 queued, 0 success**.
Ця вибірка не доводить відсутності успішного запуску за всю історію репозиторію.

## Останній завершений запуск: prerequisite build

Source: `16ba7e97d585062ba690eba790ecd0c9af3085fd`, run
[38955544811](https://github.com/managedcode/KeyLoad/actions/runs/38955544811).

| Подія | Час / тривалість |
| --- | --- |
| Створено run | 2026-10-16 19:31:26 |
| Перший фактичний job стартував | 20:15:47; затримка 44 хв 21 с |
| Build Docker images | 20:15:47–20:18:19; 2 хв 32 с |
| Його Build benchmark tests | 20:16:15–20:18:11; 1 хв 56 с; failure |
| Build and check KeyLoad | 20:19:02–20:21:11; 2 хв 9 с |
| Його Build KeyLoad | 20:19:33–20:21:08; 1 хв 35 с; failure |
| Оновлено terminal run | 20:21:12; wall time 49 хв 46 с |

17 jobs: один success, два failure, 14 skipped. Цей run не містить запущеного
database workload. API `startedAt` самого run дорівнює `createdAt`; для реального
початку виконання використано job timestamps, а не назву поля run.

Оригінальні помилки у
[Docker-image build job](https://github.com/managedcode/KeyLoad/actions/runs/38955544811/job/117162313430)
та
[solution build job](https://github.com/managedcode/KeyLoad/actions/runs/38955544811/job/117162313445)
вказують на `KeyLoad.Clustering`: CA2213/CA2000 disposal ownership, CA1508,
CA1859, IDE0031, IDE0059, IDE0251 та пов'язані diagnostics. Збережено 24 унікальні
повідомлення з точними файлами й позиціями. Це compiler/analyzer gate failure;
його не можна пояснювати повільною базою, коротшим workload timeout або
прибирати вимкненням правил.

## Тривалий запуск 15–16 жовтня

Source: `9fb574bd8e21d54eaa08328db8956c297a05d82b`, run
[38811914189](https://github.com/managedcode/KeyLoad/actions/runs/38811914189).
Створено 2026-10-15 21:42:36, terminal update 2026-10-16 10:15:45:
**12 год 33 хв 9 с**.

GitHub API повернув усі **1 904 jobs**: 1 901 failure, два success, один skipped.
З них **1 898 matrix workers** визначено наявністю `Run database workload`.
KeyLoad мав 178 cells; кожна з десяти інших іменованих database groups — 172.
Це фактично створені jobs цього source cohort, а не припущення з поточного planner.

| Етап | Оригінальні timestamps / результат |
| --- | --- |
| Перший job: Build Docker images | 22:34:13–22:55:37; success; очікування від створення 51 хв 37 с |
| Plan benchmark runs | 22:42:27–22:42:38; success |
| Build and check KeyLoad | 22:42:40–23:13:35; failure |
| Build KeyLoad | 22:43:08–22:53:06; 9 хв 58 с; success |
| Check code formatting | 22:53:06–23:03:50; 10 хв 44 с; success |
| Test benchmark contracts | 23:03:51–23:13:28; 9 хв 37 с; failure |
| Перший завершений failed matrix job | OpenSearch / Check / 3 nodes; 23:10:11 |
| Останній completed job | 2026-10-16 10:15:44 |

[Contract job 116707016756](https://github.com/managedcode/KeyLoad/actions/runs/38811914189/job/116707016756)
запустив 1 322 tests: 1 313 passed, **9 failed**, zero skipped. Failures:

- П'ять `AcIso007FailedNativeChildRetainsBoundedPrivateEvidenceThenJoinsHealthyContinuation`
  для `stderr-bound`, `credential-tail`, `retention-conflict`, `exit`, `credential`:
  очікувався exit code 0.
- Два `ActualLinuxPermissionFaultPreservesInitiatingEvidenceAndSurfacesCleanupOnlyFailure`:
  `The original native stream fixture inventory changed.`
- `AcTsi005DefaultSuccessWrappersFailButRealEmptyResultsRemainValid`:
  замість `KeyLoadTimeSeriesIntensiveReplyException` отримано `ArgumentNullException`.
- `AcChunk006ActualExternalConsumerRunsAllThirtySixMatchedCases`: очікувався exit code 0.

Ці конкретні падіння потребують своїх виправлень і meaningful regressions;
наявність інших 1 313 passing tests не кваліфікує suite.

### Чим насправді були зайняті workers

| Step | Кількість виконаних | Сума секунд усіх jobs | Median | Max | Результат |
| --- | ---: | ---: | ---: | ---: | --- |
| Prepare database containers | 1 898 | 174 804 | 84 с | 914 с | 1 897 success, 1 failure |
| Build benchmark tests | 1 864 | 304 500 | 173 с | 223 с | усі success |
| Run database workload | 1 898 | 3 199 | 2 с | 5 с | усі failure |

Це суми step elapsed time, **не billing minutes** і не CPU time. Вони можуть
перекриватися між runner jobs. Максимальний observed overlap matrix job intervals
становив 20; це не доказ account concurrency limit і не native TUnit concurrency.
34 matrix workers не виконували build step; таблиця не приписує їм успішну збірку.

Representative original
[OpenSearch worker 116715158130](https://github.com/managedcode/KeyLoad/actions/runs/38811914189/job/116715158130)
отримав `DocumentComparisonSelectionInvalid` у
`DocumentWorkerSelection.Read` (`DocumentWorkerSelection.cs:42`), через
`ComparisonWorkerSelection.ParseSelection` (`ComparisonWorkerSelection.cs:116`).
Тест `SelectedNativeCaseRunsCommonIntensiveWorkload` впав за 17 мс.
Потім finalization не створила worker evidence, а upload повідомив відсутні файли.
Це відмова selection/configuration перед вимірюванням. Усі 1 898 step failures
підтверджені jobs metadata, але причина кожного не встановлена одним representative log.

### Чому failure продовжував споживати runners

Immutable
[workflow цього source](https://github.com/managedcode/KeyLoad/blob/9fb574bd8e21d54eaa08328db8956c297a05d82b/.github/workflows/benchmarks.yml)
визначав для database matrix `needs: [comparison-plan, comparison-images]` і
`fail-fast: false`. `comparison-build` був prerequisite лише фінальної aggregation
та website stage. Тому failed contract suite не блокував уже розгорнуті matrix jobs.
Спільна перевірка worker selection перед підготовкою images могла виявити
configuration failure до масового fan-out. Незалежні native preflight та workload
matrices зберігають паралельне виконання; blanket chaining не випливає з цього доказу.

Повторювані 1 864 builds і container preparation після відомих ранніх failures
пояснюють значну частину runner роботи. Спільний readiness/selector gate перед fan-out
та reuse exact-source compiled bundle мають перевірятися в Linux GitHub Actions
без послаблення inventory, ізоляції runner/topology, correctness або provenance.
Зменшувати datasets чи measured operations для отримання green не випливає з доказів.

## Queue і cancellation

На початковому snapshot
[38961164019](https://github.com/managedcode/KeyLoad/actions/runs/38961164019)
був queued із трьома queued prerequisite jobs; workload ще не працював.
Це моментний стан, а не твердження про його поточний або майбутній результат.

Після довгого run 38811914189 у bounded history було 15 cancelled runs;
разом із попереднім 38784824291 — 16. Не слід змішувати ці різні інтервали.
Representative
[38958742674](https://github.com/managedcode/KeyLoad/actions/runs/38958742674)
створено 19:57:46, cancelled 20:17:29, **jobs: []**. Отже його 19 хв 43 с
не були виконанням benchmark. Наступний push run створено 20:17:27, за дві
секунди до cancellation; це узгоджується із заміною pending run в одному
concurrency group, але receipt не ідентифікує автора cancellation.

Workflow запускався на кожний push до main; group `keyload-benchmarks-${{ github.ref }}`
мав `cancel-in-progress: false`. Queue pressure і superseded pending cohorts слід
відділяти від actual worker execution. Наявні receipts не визначають точний
account-wide runner quota чи внесок інших workflow; це потребує окремого доказу.

## Межі доказів і completion gate

Цей документ не підтверджує успішного repair, проходження всіх tests, usable RF3,
measurement completeness, regression-free performance чи website eligibility.
Після source repair потрібні оригінальні Linux GitHub receipts для exact delivered
source: build/analyzers/format/governance/contracts, native preparation/preflight,
повний дозволений canonical inventory, caller-visible correctness, isolated workers,
Aspire-owned topology/lifecycle/cleanup та strict aggregation з повним обліком кожної
обов’язкової cell. За ADR-080 повний authenticated cohort може містити failed/null
cells: числові measurements походять лише з успішних qualified cells, failed cells
залишаються null із фактичною причиною. Missing, corrupt і mixed-source cohorts
відхиляються. Публікація metrics можлива лише після цієї перевірки.

Raw evidence збережено локально в ignored repository-owned
`artifacts/benchmark-pipeline-repair/evidence/`: `runs-100.json`, full selected
run/jobs JSON, original bounded selected job logs, immutable workflow та похідний
summary. `sha256.json` фіксує байти локальних receipts. Artifacts не є committed
qualification і можуть бути недоступні іншому checkout; наведені GitHub URLs і
source commit дають змогу незалежно перевірити висновки.

## Owner correction та source replacement, 2026-10-17

[ADR-142](../ADR/ADR-142-shared-live-benchmark-sessions.md) і
[SessionExecution](../Features/BenchmarkComparisons/SessionExecution.md) фіксують
новий дизайн: два measurement jobs на базу, native один/три вузли, багато окремих
TUnit cases на одній живій Aspire infrastructure, вимірюване завантаження мільйона,
потім операції по 100k без warmup і per-case reset. Повний набір зберігає
автоматичний main-push запуск. Manual-only перенесення старих 1 898 workers
власник не прийняв як вирішення проблеми.

Власник прямо наказав видалити весь замінений benchmark code. Старі per-cell
planners, dispatch paths, top-level runners, report producers/readers та exclusive
fixtures видалені в робочому дереві; reusable native operations/oracles і bounded
provider/image transport збережені. Це source checkpoint, а не delivery proof.
Початкові сім document cases не закривають повну model-family qualification.

Застарілі queued/pending Benchmarks
[38971888904](https://github.com/managedcode/KeyLoad/actions/runs/38971888904) та
[38965965801](https://github.com/managedcode/KeyLoad/actions/runs/38965965801)
скасовані; GitHub підтвердив completed/cancelled для обох. Після окремого
foundation-коміту `abc0a1dbdd8087c9b902da0084b844f4513b8e92`, який ще містить старий
workflow, також скасовано лише
[38977009696](https://github.com/managedcode/KeyLoad/actions/runs/38977009696);
GitHub підтвердив completed/cancelled цього exact-source run.
Build and Tests і functional qualification не скасовувалися.

Нова source-конфігурація, native regression cases, compiled runtime import і
повний новий workload ще потребують exact-source Linux build/format/governance,
справжнього запуску та retained original reports. Локальні source inspection і
інтеграційна foundation compilation не закривають ці benchmark gates.
