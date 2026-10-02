# Критерії повноти документації

Мета: читач знаходить кожну функцію, її контракт, архітектурні рішення, джерела та стан перевірки без історії чату. Специфікація: [RepositoryGovernance](docs/Features/RepositoryGovernance.md); рішення: [ADR-037](docs/ADR/ADR-037-documentation-coverage.md).

## Межі та актори

У межах: рівно 20 канонічних функцій, ADR-001–039, виправлення дубльованої ідентичності ADR-034, доповнення вузьких Feature-документів, навігація й покриття всіх 104 KL-задач. Документуємо наявні source-контракти й required/planned поверхні окремо.

Поза межами: зміни runtime, публічних DTO/API чи persisted format, вибір ще не погодженого blob протоколу, встановлення скілів, запуск локальних тестів, нові performance-числа, публікація сайту або заява про readiness. Власник дозволив опис функцій; невирішені продуктові рішення не отримують неявного implementation approval.

Актори: власник продукту, читач/розробник, strongest-model архітектор, два bounded автори й лід-інтегратор. Entry points: README → docs/README.md → Feature → ADR → source/test/evidence. Runtime security, data, performance та observability не змінюються.

## Вимоги і критерії

| Вимога | Критерій та pass/fail |
|---|---|
| REQ-DOCS-001: усі канонічні функції мають окрему специфікацію | AC-DOCS-001: рівно всі 20 визначених Feature-файлів існують та індексовані; жодна owning slice чи required BlobStorage не пропущена. |
| REQ-DOCS-002: кожна функція має повний поведінковий контракт | AC-DOCS-002: кожен Feature має стабільні REQ/AC, акторів, entry points, slice map, межі/N/A, source/accepted-target/planned поділ, позитивні/негативні/граничні/error flows, test/evidence mapping і Mermaid. Втрата попередніх obligations — fail. |
| REQ-DOCS-003: усі архітектурні рішення описані змістовними ADR | AC-DOCS-003: ADR-001–039 представлені окремими файлами зі статусом, контекстом, rationale/alternatives/consequences, related REQ/AC, implementation contract, ownership/dependencies, rollout/rollback, verification і Mermaid. Невирішений вибір явно Proposed. |
| REQ-DOCS-004: рішення мають унікальну ідентичність | AC-DOCS-004: номер заголовка збігається з шляхом, дубля немає; comparisons зберігає ADR-034, foundation стає ADR-036 з актуальними посиланнями та записом виправлення. Broken policy link чи stale foundation path — fail. |
| REQ-DOCS-005: backlog та критерії мають повну трасованість | AC-DOCS-005: машинний каталог містить рівно всі 104 KL-ID з поточного status.json, кожен посилається на існуючі Feature та ADR; кожен продуктовий REQ мапиться на measurable AC і названий наявний або явно запланований автоматизований тест/manual-review exception. Пропущені KL, критерій без перевірки чи дубльований status authority — fail. |
| REQ-DOCS-006: джерела та межі готовності правдиві | AC-DOCS-006: source-present, required/planned та GitHub-qualified розрізняються; persisted auth і paused local restore описані коректно; public blobs/MCP не оголошені реалізованими; немає вигаданих routes, цифр або durability claims. Назва тесту не passing result; непідтверджене Implemented — fail. |
| REQ-DOCS-007: збережені політики, source та власність | AC-DOCS-007: усі 27 початкових policy hashes збережені як immutable baseline; жодну політику не змінює цей documentation task. Незмінені файли мають ті самі повні хеші; для зовнішнього concurrent доповнення потрібні окремий original/current hash та доказ збереження повного старого тексту й кожного правила. Попередні Feature REQ/AC збережені; source/config/test файли не редагуються цією роботою; workers змінюють тільки призначені нові файли. Непояснений drift, втрата правила/ID чи overlapping write — fail. |
| REQ-DOCS-008: навігація, diagrams та інтегрована перевірка завершені | AC-DOCS-008: README/docs indexes/Architecture пов'язані; локальні links існують; записано start після spec/ADR/plan, COMPLETE результати двох авторів, strongest-model review, source/whitespace/governance/catalog checks і render кожної Mermaid діаграми. Частковий packet чи нерендерований граф — fail. |

## Метод перевірки і traceability

| AC | Документальна перевірка | Обов'язкові негативні/граничні випадки |
|---|---|---|
| 001/002/003/004 | Статична інвентаризація Feature/ADR, секцій/ID і foundation references; незалежний full-file огляд | Missing file, duplicate ADR number, missing REQ/AC/diagram, Proposed без unresolved boundary |
| 005 | Порівняти KL-каталог із ключами поточного status.json; перевірити всі Feature/ADR і REQ/AC/test mappings | Missing/extra KL, неіснуючий Feature/ADR, criterion без перевірки |
| 006 | Full-file source/test review і названа evidence boundary | Наявний auth не названий absent; snapshot chunks не названі blobs; future tests не названі passed |
| 007 | Повні original/current SHA256 всіх політик, full-text preservation при поясненому зовнішньому доповненні, старі Feature IDs і exact worker ownership | Непояснений policy drift, зміна політики цим task, втрачений старий ID, source write |
| 008 | Local link/index inventory; `node scripts/Features/RepositoryGovernance/verify.mjs`; `git diff --check`; cached Mermaid CLI render; joined review | Відносний шлях, anchors, нерендерований graph, неприєднаний required worker |

Документальні criteria отримують автоматизовані статичні перевірки та явний manual full-diff/source-review exception: runtime тест не може довести правильність опису невирішеного ADR. Це не виняток до продуктового TUnit/CI-only правила і не альтернативний test framework.

Повний продуктовий baseline — успішний [GitHub CI 36926803549](https://github.com/managedcode/KeyLoad/actions/runs/36926803549) для `9c570f8c33a7a9667507a8e1c0ca68860de3be45`. Він не кваліфікує спільні незакомічені зміни. Поточний complete-source build/format та подальші unit/recovery/RF3 gates залишаються у відповідних продуктових планах; жоден не може бути пропущений чи перейменований на passing через документацію.

Міграція: додати відсутні описи й links; зберегти існуючі IDs та політики. Rollback тільки нового документального тексту після review, без видалення попередніх правил або immutable CI evidence. Немає міграції даних чи поведінки.

Acceptance revision 2026-10-02, before final verification: у shared checkout інші owning tasks додали повтор того самого experimental Orleans rule до root Global Skills, Search та ResourceExecution ownership до Core policy та ResourceExecution acceptance ownership до UnitTests policy. Root diff показує тільки insertion; Core зберігає весь попередній 2334-byte prefix, UnitTests — 2716-byte prefix з original SHA256. Цей documentation task не писав у AGENTS.md. AC-DOCS-007 уточнено для зовнішньої additive зміни; початковий snapshot не замінюється, видалення/послаблення правил або неперевірений drift залишаються fail. Final strongest review мусить оглянути всі три доповнення та full hashes. Історичний ADR-034 foundation reference лишається явним policy conflict у ADR-036/index, без переписування правила.
