# KeyLoad
## Архітектура, дослідження та план розробки

**Дата:** 16 вересня 2026 року  
**Статус:** запропонований інженерний дизайн, редакція 0.3; API ще підлягає реалізації  
**Цільовий стек:** C# / .NET 10, Microsoft Orleans, ZoneTree  
**Моделі:** JSON/KV, індекси, property graph, часові ряди, вектори, гібридний пошук, Event Store, durable queues і topics.  
**Розширення 0.2:** SQL, unified planner, clients/grains, users/permissions, sensitive fields, managed ANN, sharding і PostgreSQL benchmark.  
**Розширення 0.3:** streams/replay, durable delivery, consumer groups, inbox/outbox, shared TransactionDomain, persisted scheduling та eventing recovery.  
**План робіт:** 104 задачі KL-001..KL-104; реалізація і вимірювання ще попереду.

Цей документ містить результати перевірки публічної документації, вибіркове читання API ZoneTree та запропоновані рішення для KeyLoad. Прототип, навантажувальні тести, перевірки відмов і повний аудит коду залежностей у межах цього дослідження не виконувалися. Числові межі в розділах про тести й бюджет запитів є початковими інженерними цілями. Версії пакетів та source commits потрібно зафіксувати в KL-001 перед реалізацією.


### Що змінилося у редакції 0.3

Event Store і durable queues включені до раннього kernel. Додано 10 розділів, 24 задачі KL-081..KL-104 та ADR-023..ADR-031. Спільний TransactionDomain формалізує atomic document + event + enqueue, а inbox/lease contract визначає protected processing effects. Описано topics, checkpoints, delayed delivery, privacy та paused restore. PostgreSQL baseline розширено Marten/Wolverine і messaging workload. Редакція 0.2 збережена окремими файлами.

Числа та гарантії нижче є специфікацією для реалізації й qualification. Додані механізми не виконувалися як прототип у межах редагування документа. Нові джерела S46..S56 перевірені для цього розширення; джерела S1..S45 збережено з попередньої редакції.

### Історія редакції 0.2

SQL-підмножина входить до раннього query scope. SQL, JSON і C# використовують один authorized AST. Деталізовано три ролі графа у пошуку, global branch merge, managed-first ANN, atomic/physical partition boundaries, session grain concurrency, permissions і masking. Додано PostgreSQL baseline, 36 нових задач та ADR-012..ADR-022. Попередні 44 задачі й storage/recovery contracts збережені з необхідними уточненнями.

### Навігація

| Питання | Розділи |
|---|---|
| Storage, транзакції, відновлення, базовий roadmap | 1–20 |
| Компоненти і сучасні бази для порівняння | 21–22 |
| SQL, JSON/C# API, planner і execution | 23–25 |
| Тримодальний пошук та алгоритми | 26–27 |
| Sharding, replicas, migration, regions | 28 |
| Users, policies, sensitive fields | 29–30 |
| Швидкість, PostgreSQL benchmark, product scope | 31–33 |
| Порядок реалізації, додаткові задачі, ADR | 34–36 |
| Event Store, work queues, topics і consumer groups | 37–40 |
| TransactionDomain, atomic processing, Orleans і scheduler | 41–42 |
| Event/queue SQL, SDK, PII, retention і restore | 43–44 |
| Eventing benchmarks, порядок робіт, KL-081..KL-104 | 45–46 |

## 1. Продуктовий контракт

KeyLoad задуманий як самодостатня all-in-one платформа даних і надійної доставки для .NET-систем: багатомодельна база, Event Store та durable message broker. Основна одиниця моделі: сутність, її документ, зв'язки, event stream, задачі та пошукові представлення. Модулі поділяють каталог, permissions, transaction domains і recovery contracts.

Початковий наскрізний сценарій: `Company → Project → Document → Chunk`. Документ містить атрибути та походження; ребра задають зв'язки; часові ряди зберігають timestamped samples і метрики; Event Store зберігає бізнес-факти й версії streams; черги керують доставкою задач; векторні поля дають семантичний пошук; текстовий індекс дає lexical search. Запит може обмежувати результати tenant, проєктом, графовим оточенням, періодом часу та правами доступу.

### Режими запуску

| Режим | Склад | Ціль |
|---|---|---|
| Embedded | Storage core та моделі в процесі застосунку | Локальні інструменти, тести, невеликі застосунки |
| Standalone | Один процес сервера, один Orleans silo, локальний каталог даних | Перший самостійний сервер |
| Cluster | Кілька вузлів, репліковані partition groups, Orleans, внутрішній metadata service | Горизонтальне масштабування та відмовостійкість |

Зовнішні PostgreSQL, Redis, Elasticsearch, Kafka чи окремий векторний сервер не є обов'язковими залежностями. PostgreSQL використовується як benchmark-конкурент. Опційні embedded-бібліотеки можуть мати власні файли в каталозі KeyLoad. Базова конфігурація managed-first; для ANN кваліфікуємо managed реалізацію. USearch із C++ ядром і C# binding залишається optional research baseline. Transitive/native залежності перевіряються окремо в KL-001. [S11][S12]

### Межі першого релізу

Перший product milestone: документи, strict indexes, append-only streams із expected revision, durable enqueue/claim/ACK та базові retry, atomic document + event + queue-lane batch, persisted idempotency, scalar SQL, JSON/C# API, permissions/PII omission і offline backup/restore. Перед real-data usage всі eventing outputs проходять security gates. Inbox/CommitProcessing та subscriptions вводяться наступним bounded slice. Product versioning визначається окремо від редакції документа; порядок уточнює розділ 45.4.

Наступний eventing slice додає inbox/atomic processing, delayed scheduler, consumer groups і checkpoints; далі розширюються граф, часові ряди, exact/ANN, BM25 і hybrid search. Дослідний три-вузловий replication spike починається після появи мінімального коректного ядра.

До окремих майбутніх проєктів належать: повна PostgreSQL SQL/wire compatibility, Cypher/SPARQL-сумісність, глобальні serializable transactions, глобальні unique constraints через довільні partition, active-active multi-region writes, OLAP із великими joins, автоматичне виконання довільного коду користувача. Рання SQL-підмножина й versioned graph/search extensions описані у розділі 23.

## 2. Що підтверджено джерелами

### ZoneTree

ZoneTree надає впорядковане persistent key-value storage, seek/range-ітератори, оптимістичні багатоключові транзакції, серіалізатори, comparers, WAL і compaction. MIT-ліцензія зазначена в репозиторії. Цього достатньо для побудови локального storage adapter; модель документів і розподілені гарантії проєктує KeyLoad. [S1][S2]

`AsyncCompressed` є типовим WAL. У документації `SyncCompressed` описано окремий tail writer, типовий інтервал якого становить 500 мс. `Sync` має синхронний stream write path; повна стійкість до втрати живлення залежить від flush-контракту та storage stack. Конфігурація з назвою Sync сама по собі не є доказом durable ACK. [S3]

Вбудований live backup покриває non-transactional trees. Для transactional trees документація прямо вимагає окремої стратегії. Початкове рішення KeyLoad: закритий, узгоджений backup усіх data/transaction-log/metadata файлів. [S4]

`IteratorType.Snapshot` перемикає mutable segment і читає зафіксований read-only зріз. `NoRefresh` може бачити пізніші зміни у поточному mutable segment. Snapshot-ітератор може спричиняти додаткові segment moves, тому його вартість входить у benchmark. Узгодження snapshot-читання з transaction visibility перевіряється окремо. [S5]

### Orleans

Orleans відповідає за actor identities, routing та activation lifecycle. Типовий grain directory допускає короткі дублікати activation під час нестабільності. Сильніший directory наявний як experimental/preview можливість у поточній документації. Стан directory слід відокремлювати від durable authority для partition data. [S6][S7]

Orleans має distributed transactions для transactional grain state. Інтеграція потребує відповідного state storage. Для KeyLoad це окремий integration option: автоматичне покриття довільних ZoneTree, ANN-файлів та локальних дисків цим механізмом не встановлене. [S8]

### Пошук і consensus

`ZoneTree.FullTextSearch` документує token/record indexing та boolean-пошук. Наявність BM25 у переглянутому README не підтверджена; необхідна окрема перевірка реалізації. Lucene.NET документує `BM25Similarity`, тому його варто включити як embedded-кандидат. [S9][S10]

USearch має C# SDK, vector search та save/load/view із native C++ ядром. Він залишається дослідним baseline. Для базового managed-first deployment потрібно кваліфікувати окрему managed ANN реалізацію; API, deletion, concurrency, persistence і memory ownership перевіряються на pinned source/package versions. [S11][S12]

.NET бібліотека .NEXT надає Raft primitives, persistent WAL, read barriers і snapshots. Її документація описує background flush та пов'язує durability з liveness кворуму. Для контракту KeyLoad `DurableQuorum` потрібна додаткова перевірка збереження журналу, term та vote до відповідних підтверджень, включно із втратою живлення всіх процесів. .NEXT залишається кандидатом до проходження цієї перевірки. [S13][S14]

RRF об'єднує ранжовані результати різних retrievers. Це придатна стартова схема для hybrid ranking KeyLoad; конкретні ваги і candidate windows визначаються relevance-тестами. [S15]

## 3. Рекомендована архітектура

```text
.NET SDK / HTTP API / optional gRPC
                 │
        Query and Command Gateway
       auth, validation, routing, limits
                 │
     ┌───────────┴───────────────────────┐
     │                                   │
Orleans coordination                Query execution
catalog operations, jobs,           planner, bounded fan-out,
routing, placement intent           merge, traversal, ranking
     │                                   │
     └───────────┬───────────────────────┘
                 │
       PartitionHost on each node
      explicit ownership + writer queue
                 │
     ICommandCommitter / ordered apply
      local transaction or quorum log
                 │
       ZoneTree storage adapter
        documents + events + queue state
        strict indexes + inbox/outbox
                 │
     ┌───────────┴───────────────────────┐
     │                                   │
Derived text index                 Derived ANN index
version + checkpoint               version + checkpoint
```

### Розподіл відповідальності

`PartitionHost` є node-local сервісом, який володіє engine instances, файловими handles, maintainer, reader leases та чергою запису. Його життєвий цикл прив'язаний до replica ownership. Деактивація grain не є командою видалити чи перенести каталог даних.

`PartitionGrain` маршрутизує команди та координує роботу partition. Storage calls проходять через writer authority. Кількість grains визначається кількістю операційних partition і jobs. Grain на кожен документ може бути опційним facade; базовий data path працює з partition batches. Для кожного ребра, sample або ANN-вузла окремий grain у базовій архітектурі не створюється.

`QueryCoordinator` планує запит, обмежує fan-out, збирає top-k та керує загальним бюджетом. CPU-heavy traversal, scoring, vector distance та compaction мають окремі bounded execution pools. Під довгими grain turns чи write gates не виконуються зовнішні API-виклики.

`CatalogService` зберігає collection/event/message schemas, TransactionDomain bindings, queue/subscription configuration, index generations, routing map, replica sets та format versions. У кластері його authoritative state знаходиться у внутрішньому metadata consensus group.

### Bootstrap без зовнішньої бази

Сервер стартує з `clusterId`, persistent `nodeId`, локального каталогу, адреси internal transport та seed nodes. Спочатку стартує metadata transport/consensus. Після readiness підключається Orleans membership adapter. Adapter спілкується з локальним metadata endpoint напряму, тому bootstrap не залежить від grain-виклику в ще не запущеному Orleans.

Зміни membership мають CAS/epoch semantics і зберігаються персистентно. У standalone режимі використовується локальний одновузловий варіант. Dynamic Orleans placement для storage-owning grains обмежується ownership map; перенос даних виконує контрольований migration job.

## 4. Partition model і фізичне зберігання

Ідентичність сутності: `(TenantId, CollectionId, PartitionKey, EntityId)`. SDK повинен передавати partition key або мати явний routing lookup. Пошук тільки за довільним EntityId без routing information має окремо визначену вартість. У редакції 0.2 AtomicPartitionId позначає незмінний transaction scope; physical shard є одиницею engine/replica ownership і може містити кілька atomic partitions. Точні межі визначені в розділі 28. Shared domain resolution для документів, stream sets та queue lanes описано у 41.1; його contract приймається в P0. Parent database scope є частиною повної resource identity.

Початкова affinity: tenant + collection + application partition key, наприклад projectId. Великі tenants можуть мати багато partition. Глобальний hash(EntityId) варто оцінювати для random CRUD; project/graph affinity зменшує кількість переходів між вузлами. Компроміс між locality та hotspot розглядається для конкретного workload.

Logical buckets і physical ZoneTree instances є різними рівнями. Їх кількість підбирається через вимірювання. Кожен відкритий engine споживає RAM, cache, handles і compaction resources, тому фіксувати тисячі окремих trees до benchmark недоцільно.

### Namespaces

Нижче наведено логічний вигляд ключів. На диску використовується versioned binary encoding. Для physical shard із кількома atomic partitions ключі мають tenant/atomic-partition namespace; у 1:1 layout незмінні префікси можуть бути частиною descriptor. Зовнішня ідентичність завжди включає весь scope.

| Namespace | Ключ у межах partition | Значення |
|---|---|---|
| Document | `D / collection / id` | Envelope + immutable UTF-8 JSON |
| Scalar index | `I / indexId / typedValues / id` | Revision або covering fields |
| Unique index | `U / indexId / typedValues` | Entity reference |
| Edge record | `E / graphId / edgeId` | From, to, label, attrs, revision |
| Outgoing adjacency | `EO / graphId / from / label / edgeId` | Target + edge revision |
| Incoming adjacency | `EI / graphId / to / label / edgeId` | Source + edge revision |
| Time sample | `T / seriesId / timeBucket / timestamp / sequence` | Value + event identity |
| Canonical vector | `V / collection / fieldId / id` | Model id, dimensions, metric, bytes, revision |
| Projection outbox | `O / sequence / ordinal` | Committed projection mutation |
| Deduplication | `C / clientScope / commandId` | Payload hash, outcome, revision/token |
| Apply progress | `M / lastApplied` | Committed command position |
| Event stream | `SH / streamRef`, `EV / streamRef / revision`, `ED / streamRef / eventId` | Head, immutable events та dedup |
| Event feed / snapshots | `EF / eventSequence / ordinal`, `ES / streamRef / revision / reducerVersion` | Replay positions та aggregate snapshots |
| Queue | `QB / queue / messageId`, `QM / queue / messageId` | Immutable body та mutable delivery state |
| Queue indexes | `QR`, `QS`, `QL`, `QD` | Ready, scheduled, lease expiry, dead-letter references |
| Subscriptions / inbox | `SG`, `SC`, `SD`, `IN` | Config, checkpoints/gaps, deliveries, processing receipts |

Документ, його strict indexes, canonical vector та outbox event знаходяться в одному transactional scope. Catalog може прив’язати до цього самого AtomicPartitionId domain events і queue lane; producer/consumer atomic batches описані у розділі 41. Повний event/queue keyspace наведений у 39.2. Фізичне винесення time buckets чи projections створює окремі storage scopes; API не повинен приховувати цю межу.

### Envelope

Обов'язкові поля: `FormatVersion`, `Revision`, `CreatedAtUtc`, `UpdatedAtUtc`, optional `ExpiresAtUtc`, payload length та checksum там, де це потрібно для recovery/backup protocol. Embeddings зберігаються окремими typed vector fields. Звичайний `GetDocument` повертає JSON та metadata; завантаження великих векторів є явною операцією.

Buffers стають незмінними після передачі storage layer. ArrayPool-буфер можна повертати лише після завершення lifetime усіх readers та storage owners. Контракт ownership важливіший за бажання прибрати одну copy без вимірювань.

### Ordered key encoding

KeyCodec має власну версію та golden test vectors. Він визначає type tags, окреме кодування missing/null, signed integer ordering, UTC time ordering, string collation, escaping та arrays. Простий length prefix рядка не гарантує лексикографічного порядку. Для float визначаються NaN, infinity та signed zero. У першому індексному API допустимо обмежити numeric types до чітко визначених int64/decimal/double policies.

Comparers та serializers є частиною довготривалого формату даних. Їх зміну слід проводити через migration/reindex із перевіркою сумісності. [S2][S5]

## 5. Write path, читання та транзакції

### Single-node apply

1. Gateway перевіряє auth scope, JSON, schema, розмір та command identity.
2. Writer queue серіалізує команди partition; bounded queue дає backpressure.
3. Виконується dedup lookup; повтор із тим самим ID та іншим payload завершується explicit conflict.
4. В одній транзакції перевіряються всі preconditions, формуються index deltas, змінюються canonical records, за наявності команд додаються domain events/queue state/inbox receipt, додається outbox event, записуються outcome та `lastApplied`. Shared TransactionDomain перевіряється до apply.
5. Підтвердження залежить від оголошеного durability profile та перевіреного flush barrier.
6. Projection workers читають тільки committed outbox records.

Усі canonical writes проходять через цей механізм. Окремі прямі `Upsert` у underlying tree обходили б встановлені інваріанти.

### Read contract

Point read використовує committed visibility. Strict index query мусить бачити узгоджений зріз index + document. Наявність `Snapshot` у звичайного ZoneTree та point-read helpers у transactional API потребує окремого proof-of-concept для transactional range queries. Переглянутий `ITransactionalZoneTree` надає transactional/committed point access; читання raw underlying tree без adapter не вважається достатнім доказом snapshot isolation. [S2][S5][S17]

Безпечний стартовий варіант для bounded scans: спільний read/apply gate, завершені або відкотені transactions перед читанням та короткий узгоджений scan. Оптимізований committed snapshot adapter вводиться після property/recovery tests. Long scans, exports і index builds використовують контрольовані checkpoints або перевірені immutable read views; вони не утримують grain turn на весь export.

### Матриця гарантій

| Операція | Початковий контракт |
|---|---|
| Point get | Latest committed local/primary value; у cluster strong read із barrier |
| CAS/Put/Delete | Atomic operation у межах partition |
| Batch write | All-or-nothing у межах одного partition |
| Strict scalar/unique index | У тій самій транзакції з документом |
| Unique constraint | Explicit partition scope; global scope допускається лише для single-partition collection |
| Multi-partition bulk | Per-partition outcomes, можливий partial success |
| Graph edges within partition | Edge та дві adjacency змінюються атомарно |
| Cross-partition edges | Окремий явно асинхронний протокол, eventual reverse adjacency |
| Text/ANN | Derived projections, видимість через watermark |
| Cross-partition query | Vector of partition cuts; глобальний serializable snapshot відсутній у початковому контракті |
| AppendEvents | Atomic batch із stream-revision precondition та persisted dedup |
| Document + events + local queue | Atomic за однакового AtomicPartitionId і catalog domain binding |
| Queue receive / ACK | Persisted claim/lease та fenced ACK; at-least-once delivery за retry/retention policy |
| CommitProcessing | Inbox + protected DB effects + ACK атомарно у спільному partition |
| Remote output | Source intent committed; target enqueue/dedup окремо, статус OutputPending |
| Stream/topic subscription | Per-source contiguous checkpoint; cross-partition cursor vector |

### Durability profiles

`Buffered`: явний режим для disposable/development workloads із ризиком втрати останніх підтверджених змін. `LocalDurable`: ACK після встановленого persistence barrier; захист залежить від справного persistent storage. `QuorumDurable`: підтверджена durable більшість реплік плюс локальний apply для відповіді.

Профіль задається для database/partition group. Приховане зниження гарантій окремими запитами заборонене. Публічне ім'я Durable дозволене після перевірки full path: data WAL, transaction log, metadata/manifest, flush ordering та recovery на конкретному storage contract.

## 6. Replication і failover

### Consensus boundary

Replica group має leader і followers. Спочатку використовується три-вузлова група. Кількість незалежних consensus groups росте контрольовано; grain identities не перетворюються автоматично на окремі Raft groups.

Команда отримує `GroupId`, `Term`, `LogIndex`, `CommandId`, `SchemaVersion`, canonical input та явно призначені timestamp/ID. State-dependent CAS і unique checks виконуються детерміновано в порядку apply. Можлива відмова бізнес-операції є збереженим результатом committed command.

Follower успішно підтверджує durable replication лише після необхідного persistence barrier. Leader повертає `QuorumDurable` після durable majority і застосування команди до власного committed state. Persisted term/vote та membership configuration є частиною доказу безпеки. Одного flush на leader для цього контракту недостатньо.

Для .NEXT gate перевіряє можливість реалізувати цей контракт через відповідний persistent-state adapter. `FlushInterval = 0` описує запуск background flush; сам цей параметр не доводить durable-before-ACK. Негативний результат gate веде до іншої audited consensus/WAL реалізації або явно слабшого дослідного профілю, який не можна випускати під назвою QuorumDurable. [S13][S14]

### Fencing

Storage writer перевіряє authority term/epoch. Прострочений grain чи старий leader не може виконувати нові canonical mutations. Fencing поширюється на фонового projection writer під час зміни index generation і ownership. Commit authority має зберігатися в consensus/storage layer незалежно від числа actor activations.

За втрати кворуму нові strong writes припиняються. Follower reads мають окремий declared stale mode або проходять read barrier. Клієнтський timeout означає невідомий outcome, який перевіряється/retry з тим самим CommandId.

### Recovery і snapshots

Apply position записується атомарно з усіма змінами команди. Після restart відновлюється storage, завершуються recovery actions для uncommitted local transactions, відтворюються committed commands, перевіряються apply position та schema version. До завершення recovery partition не обслуговує normal reads/writes.

Snapshot містить format/catalog epoch, replica configuration, last included log index/term та canonical state. ANN/FTS snapshots можуть прискорювати запуск; canonical records дозволяють їх відновити. Raft log видаляється лише після підтвердженої придатності snapshot для recovery. Retention outbox враховує всіх projection consumers та активні rebuild jobs.

### Rebalancing

`Prepare destination → checkpoint/copy → catch up → validate → ownership barrier → switch routing → release old replica`.

На кожному кроці є durable state та restart behavior. Старі файли видаляються після перевірки нового ownership і завершення reader leases. Transport snapshot і control messages мають authentication та integrity checks.

## 7. Document storage та scalar indexing

Документи зберігаються як валідований UTF-8 JSON. v0.1 визначає handling duplicate property names, максимальну вкладеність, числові правила та максимальний document size. Пропозиція: відхиляти duplicate names, unsupported numeric cases і documents понад configured maximum.

Початкові індекси: equality, ordered range, composite, unique-in-partition. Наступні: covering, partial, multikey arrays, exists/missing, schema-specific computed fields. Dynamic indexing усіх довільних полів може спричинити неконтрольовану кількість записів, тому v0.1 має explicit index definitions.

Update читає старі indexed values, видаляє застарілі index keys і додає нові. Sparse/null semantics визначаються у definition. Index generation має immutable schema/analyzer/keycodec version. Розбиття single-partition collection із global unique constraint на кілька partitions потребує окремого протоколу збереження унікальності; без нього split відхиляється.

Online rebuild: consistent cut + retained committed deltas → build generation N+1 → catch up → validate → atomic catalog swap → retire N після reader leases. Якщо atomic cut не доведено, перший rebuild виконується в maintenance window.

## 8. Property graph

Вершини є entity references на документи. Ребро має EdgeId, From, To, Label, Attributes та Revision. Окремий edge record дає прямий доступ за EdgeId; дві adjacency структури забезпечують outgoing/incoming scans.

v0.x: neighbors, labeled traversal, bounded BFS, unweighted shortest path. Для traversal визначаються maxDepth, maxVisitedNodes, maxVisitedEdges, deadline, memory budget та cancellation. Алгоритм batch-fetch отримує сусідів для frontier; per-edge RPC у storage path відсутній.

Пропозиція початкових захисних default limits: maxDepth=5, maxVisitedNodes=10 000, maxVisitedEdges=100 000. Ці значення є settings і перевіряються навантаженням. Вичерпання бюджету повертає `BudgetExceeded` або явно `PartialResult` за opt-in.

Cross-partition edges впроваджуються після cluster core. Canonical owner визначається для edge/outgoing record; reverse adjacency оновлюється через ідемпотентний outbox із revision і tombstone. Статус projection consistency видимий API. Referential checks і delete policy (`Restrict`, `Detach`, async cascade) задаються явно. Cross-partition атомарне видалення великого графа потребує окремого протоколу.

## 9. Часові ряди

Логічний ключ містить SeriesId, time bucket, timestamp та sequence/event identity. Один timestamp не є унікальним ідентифікатором sample. Час зберігається в UTC з визначеною точністю.

v0.x API: AppendBatch, ReadRange, ReadLatest, AggregateWindow, retention policy. Idempotency має EventId або command-scoped sample ordinals. Серії мають metadata/tags; cardinality tags і число активних series обмежуються квотою.

Початкова реалізація зберігає samples у спільному ordered engine. Chunk compression, окремі time-bucket trees і rollups вводяться після вимірювань. Для великих потоків storage layout із chunks зменшує per-record overhead; формат chunks має checksum/version та late-arrival policy.

Rollups зберігають sum/count/min/max і власний watermark. Avg обчислюється з sum/count. Percentile rollup потребує mergeable sketch та documented approximation. Пізні events можуть оновлювати відкриті buckets або проходити correction pipeline.

TTL виконується через єдину логіку видимості та replicated expiration mutations. Довільні wall-clock delete delegates у кожній репліці не повинні самостійно змінювати canonical state. Видалення bucket очікує завершення reader leases; фізичне звільнення файлів вимірюється.

## 10. Vector storage і ANN

Кожен vector field має Dimensions, Metric, ModelId/ModelVersion та NumericRepresentation. Несумісні embeddings з різних vector spaces зберігаються в різних logical fields/index generations. API перевіряє розмірність та допустимі числові значення.

Canonical vector bytes знаходяться поруч із entity у transactional storage namespace. ANN structure є derived projection. Це дає можливість rebuild, re-quantization та заміни бібліотеки без втрати original vectors.

Перший алгоритм: exact SIMD search для ground truth, малих collections та селективних allowlists. Наступний: managed HNSW candidate з перевіреними layout, save/load, concurrency, delete/reinsert і filtered retrieval. USearch може бути optional external comparison; його native ядро не є mandatory dependency базової конфігурації. Реалізації та platform dependencies проходять KL-030 і KL-059. [S11][S12][S42]

Пам'ять raw float32: `N × dimensions × 4 bytes`. Для 1 000 000 векторів по 1536 компонентів це 6 144 000 000 bytes, близько 5.72 GiB на копію. Три копії потребують близько 17.17 GiB лише для raw vectors. Додатково потрібні ANN graph, identifiers, documents, caches, segment metadata і temporary rebuild state. Це розрахунок розміру, незалежний від результатів benchmark.

Filtered search має забезпечувати tenant/ACL/scalar/graph scope. Для малого candidate set використовується exact scoring; для великого дозволеного set оцінюється filtered ANN, oversampling і candidate expansion. Post-filtering короткого global top-k може дати неповну видачу. Фінальна authoritative перевірка document existence, revision та auth scope обов'язкова.

Метрики: recall@k щодо exact ground truth із тим самим filter, p50/p95/p99, build time, RAM, disk, update/delete/rebuild impact. Початкова ціль recall@10 ≥ 0.95 задається для затвердженого dataset та окремих filter-selectivity cohorts; це ціль приймання, яка потребує вимірювань.

## 11. Full-text та hybrid search

Інтерфейс `ITextIndex` має isolate provider files/format. Варіанти: аудит ZoneTree.FullTextSearch і реалізація відсутнього ranking або embedded Lucene.NET із BM25. Обидва працюють як derived projection із durable replay/checkpoint contract. [S9][S10]

Full-text контракт: tokenization, Unicode normalization, analyzer version, field boosts, term frequency, document length, optional positions/phrase support. Для Ukrainian та English потрібен власний test corpus. Stemming/stop words є configuration, яка впливає на index generation.

Hybrid pipeline:

```text
authenticated tenant + filter + graph scope
                    │
             candidate planning
                    │
          ┌─────────┴─────────┐
          │                   │
     BM25 candidates      vector candidates
          │                   │
          └─────────┬─────────┘
                    │
        deduplicate + weighted RRF
                    │
       current visibility / ACL check
                    │
       optional reranker + final top-k
```

Базова формула: `score(d) = Σ_i weight_i / (c + rank_i(d))`. Відсутність документа у конкретному result list дає нульовий внесок. `c`, candidate windows та weights задаються configuration; для першого експерименту можна використати c=60 і рівні ваги. Якість оцінюється на власному relevance set. [S15]

У distributed search відрізняються shard-local document frequencies. Початковий контракт може використовувати local scoring з явно documented approximation. Наступний етап: узгоджений statistics epoch для BM25 та global candidate merge для кожної modality перед RRF. Local top-k pruning обмежує global recall, тому candidate budgets і oversized shards входять у тести.

Graph-scoped search спочатку визначає дозволене graph neighborhood або передає equivalent scope у planner. MaxDepth і auth перевіряються під час traversal. Фінальні результати завжди перевіряються на canonical document state. Індекси не є самостійним джерелом дозволів.

## 12. Projection consistency

Одна canonical transaction записує зміни і committed outbox. Worker застосовує mutation і checkpoint ідемпотентно. Для external index files потрібен atomic generation manifest або replay-safe local journal, який зв'язує стан індексу з persisted watermark.

Document revision/tombstone захищають від повторного застосування застарілої події. Checkpoint просувається через contiguous applied prefix. Якщо певний event не вдалося проіндексувати, watermark не перестрибує його без явного documented error state.

`CommitToken` містить ClusterIncarnation, GroupId/PartitionId, Position, RoutingEpoch. Composite token для multi-partition роботи містить кілька позицій. `WaitForIndex(token, timeout)` завершується тільки після catch-up потрібних projections або повертає explicit timeout/failure. Досягнення watermark гарантує freshness відповідно до контракту; ANN ranking лишається approximate.

Index generation swap, retry windows, outbox GC та backup cut враховують tokens. Після restore до попереднього snapshot старий token може бути недосяжним; API повертає `TokenInvalidated` відповідно до cluster incarnation.

## 13. Query API

Публічні query frontends: KeyLoad SQL subset, JSON envelope і C# builder. Вони транслюються у спільний typed query AST. Сервер виконує binding, field-use authorization, capability checks і planning. `IQueryable` із довільним client code не входить у network contract. Повний dialect/envelope описано в розділах 23–24.

Пропонований v1 scalar request; public schema ще підлягає реалізації. Hybrid envelope наведено у 24.1.

```json
{
  "languageVersion": 1,
  "collection": "documents",
  "select": [
    "id",
    "title",
    "updatedAt"
  ],
  "parameters": {
    "projectId": {
      "type": "string",
      "value": "project-42"
    }
  },
  "where": {
    "eq": [
      {
        "field": "projectId"
      },
      {
        "parameter": "projectId"
      }
    ]
  },
  "orderBy": [
    {
      "field": "updatedAt",
      "direction": "desc"
    },
    {
      "field": "id",
      "direction": "asc"
    }
  ],
  "limit": 50,
  "options": {
    "deadlineMs": 2000,
    "allowPartial": false
  }
}
```

Tenant і principal походять із trusted authentication context. Vector query передає vector attachment у тому самому transport envelope, із dimensions/vectorSpaceId та перевіркою прав. SQL, JSON і C# frontends використовують спільні query options.

API за capability manifest: Put/Get/Patch/Delete, Batch, Query/Explain, Create/Build/DropIndex, Add/RemoveEdge, Traverse, Append/ReadTimeSeries, UpsertVector, Search, AppendEvents/ReadStream, Enqueue/ReceiveBatch/Ack/Nack/ExtendLease, CommitProcessing, Subscriptions/Checkpoints/Replay, DeadLetters/Redrive, GetCommandOutcome, WaitForIndex, Backup/Restore, Health/Diagnostics. Граматика та SDK для eventing описані у розділі 43.

Response містить результати, revision, watermark/token, used index generations, warnings та explicit partial/error state. Pagination використовує opaque cursor з sort keys, tie-break повною EntityRef, schema/routing version, expiry та integrity protection. Збереження live snapshot між сторінками має власні TTL й resource quotas.

## 14. Безпека, операції та бюджети

Tenant isolation починається у KeyCodec, routing та query planning. Transport authentication, authorization для collection/admin operations, bounded payloads, quotas та audit реалізуються до використання зі справжніми даними. Cluster transport має mutual authentication або equivalent authenticated channel.

Request limits: розмір документа/запиту, кількість batch operations, fan-out, scanned records, top-k, vector dimensions, graph traversal, query memory, deadline. Доходження межі дає явний outcome. Expensive full scan потребує explicit opt-in або administrative policy.

Node budgets: total managed/native memory, WAL backlog, projection lag, compaction work, free disk headroom, open files, pending write queue, CPU concurrency. Admission control запускається до OOM чи заповнення диска. Кожен maintainer реєструється у node-level scheduler.

Спостережуваність: end-to-end p50/p95/p99, allocs/op, GC pause, CPU, RSS/native memory, bytes read/written, compaction amplification, replica lag, apply lag, projection lag, retry/conflict rate, recovery time та corruption indicators. Логи не повинні містити document/event/message payloads, raw sensitive headers або секрети за замовчуванням. ACK/renew і delivery мають resource reservation; messaging quotas доповнюють query limits.

Backup v0.1: stop admission → drain transactions/jobs → flush → close engine/maintainer → copy all authoritative files → manifest/checksum → reopen. Restore тестується на чистому каталозі із перевіркою canonical records та index invariants. Cluster backup надалі зберігає per-partition cut vector і catalog epoch; глобальна transactional consistency через усі partitions має окремий контракт.

## 15. Performance і correctness matrix

Публічні vendor benchmarks використовуються як орієнтир для відтворення їх методики. KeyLoad measurements мають включати JSON, indexes, network, flush і replication; їх не можна заміняти цифрою isolated Upsert. [S1][S3][S16]

| Вимір | Набір для початку |
|---|---|
| Документи | 1 / 4 / 16 KiB, 0 / 3 / 10 indexes |
| Dataset | 100k / 1M; 10M або понад RAM для відповідної машини |
| Writes | Insert, overwrite, CAS, delete, batches 1 / 10 / 100 |
| Distribution | Uniform і hot-partition/skewed |
| Reads | Point, selective/nonselective ranges, concurrent snapshots |
| Vectors | 384 / 768 / 1536 dimensions, updates та deletes |
| Vector filters | 100% / 10% / 1% / 0.1% allowed candidates |
| Graph | Fan-out 10 / 100 / 10k, cycles, disconnected vertices |
| Time series | Ordered і late arrivals, high tag cardinality, retention |
| Storage | Warm/cold cache, datasets larger than RAM, active compaction |
| Deployment | Embedded, standalone network, 3-node quorum |
| Failures | Process kill, power/reset simulation, disk full, partial I/O, corruption, partitions |

Correctness oracle: прості reference models для document/index/graph/time-series, exact vector search, deterministic command history, model-based CAS/linearizability checker. Random tests зберігають seed і minimal failing history.

`kill -9` перевіряє процесний crash. Вимоги до power-loss durability потребують окремого fault model із втратою volatile OS/device state. Три процеси на одному ноутбуці дають functional cluster tests; фізично незалежні failure domains потрібні для перевірки operational assumptions.

## 16. Поетапний roadmap

| Етап | Результат | Умова переходу |
|---|---|---|
| P0: contracts і spikes | Storage/durability/read-view/backup evidence, dependency pins, TransactionDomain/eventing contracts, baseline harness | Підтверджені або явно заблоковані критичні capabilities |
| P1: standalone core | Documents, strict indexes, events, queue lane, batch/CAS/dedup, SQL, permissions/masking, recovery | Atomic document/event/enqueue; crash і safe-output invariants |
| P2: early cluster spike | Metadata bootstrap і 3-node command replication | Fencing, retries, loss of leader; durable quorum gate |
| P3: extended models | Inbox/processing, scheduler/groups, graph adjacency, TS append/range/retention | No skipped inputs/duplicate protected effects; model equivalence і budgets |
| P4: search | Text ranking, exact/managed ANN, outbox, three-way hybrid, graph scope | Rebuild consistency, recall/relevance, PostgreSQL comparison |
| P5: distributed beta | Replica placement, snapshots, movement, distributed queries | Chaos history, no lost durable commits within fault model |
| P6: production gate | Security, upgrades, backup automation, capacity guidance | Endurance, restore drills, compatibility, operational review |

Граф та search можна розробляти паралельно після стабілізації command/storage/projection contracts. Consensus, durability та format changes мають одного відповідального архітектора й обов'язкове незалежне review. Calendar estimates формуються після P0 за реальними throughput/results, кількістю розробників та deployment scope.

## 17. Backlog для розробників та AI-агентів

Кожна задача завершується кодом, тестами, коротким ADR або contract update, а для performance-sensitive paths також відтворюваним benchmark artifact. Залежності вказують мінімальний порядок. Проходження локального happy path не закриває recovery/chaos критерії.

### KL-001 · Версії, ліцензії та platform matrix

**Етап:** P0. **Залежності:** Немає.

**Робота:** Зафіксувати SDK, package locks, source commits та licenses; відокремити managed/native dependencies.

**Приймання:** Clean restore/build на Linux x64 і macOS arm64; усі dependency versions зафіксовані; unsupported platforms явно зазначені.

### KL-002 · Формальні API та consistency contracts

**Етап:** P0. **Залежності:** KL-001.

**Робота:** Описати partition identity, atomic batch, read views, durability profiles, errors та tokens.

**Приймання:** Усі операції мають scope/ack/timeout semantics; global unique і multi-partition atomic запити отримують explicit unsupported outcome.

### KL-003 · ZoneTree durability audit і crash harness

**Етап:** P0. **Залежності:** KL-001, KL-002.

**Робота:** Перевірити data WAL, transaction log, flush ordering, manifest та recovery; instrument failure points.

**Приймання:** Задокументовано durable ACK boundary; щонайменше 1000 seeded crash trials збережено; жодної підтвердженої часткової транзакції; power-loss gate має окремий результат.

### KL-004 · Transactional read view spike

**Етап:** P0. **Залежності:** KL-001, KL-002.

**Робота:** Перевірити point reads, index scans, Snapshot і transaction visibility; реалізувати safe bounded fallback.

**Приймання:** Readers ніколи не спостерігають половину document/index batch; тестовано overwrite/delete під час scan; виміряно snapshot/segment-move overhead.

### KL-005 · Backup/restore spike

**Етап:** P0. **Залежності:** KL-003, KL-004.

**Робота:** Реалізувати closed transactional backup всіх authoritative файлів та versioned manifest.

**Приймання:** Restore у чистий каталог відновлює документи, індекси, dedup та apply position; corrupted/incomplete backup відхиляється.

### KL-006 · Benchmark harness і PostgreSQL methodology

**Етап:** P0. **Залежності:** KL-001.

**Робота:** Побудувати однакові workloads embedded/standalone/cluster із параметрами WAL та індексів; визначити PostgreSQL baseline, same-quality/same-durability methodology й datasets для KL-073.

**Приймання:** Кожен результат має hardware, OS, package version, dataset seed, durability mode, raw samples, p50/p95/p99 і allocs/op.

### KL-007 · KeyCodec і storage envelopes

**Етап:** P1. **Залежності:** KL-002.

**Робота:** Реалізувати binary composite ordering, immutable payload envelope, type/null/missing semantics.

**Приймання:** 10 000 generated ordering/roundtrip cases; golden bytes збережені; mixed types, negatives, Unicode, escaping та extrema покриті.

### KL-008 · Storage adapter і lifetime

**Етап:** P1. **Залежності:** KL-003, KL-004, KL-007.

**Робота:** Інкапсулювати ZoneTree behind partition API; додати read leases, maintainer lifecycle і file locking.

**Приймання:** Другий process owner відхиляється; dispose очікує leases/jobs; pooled buffers не змінюють збережені значення.

### KL-009 · Partition writer і command envelope

**Етап:** P1. **Залежності:** KL-002, KL-008.

**Робота:** Створити bounded writer queue, deterministic apply та explicit ownership hook.

**Приймання:** Order відтворюється після replay; переповнення черги дає backpressure; external network calls відсутні під apply gate.

### KL-010 · Document CRUD та CAS

**Етап:** P1. **Залежності:** KL-009.

**Робота:** Реалізувати Get/Put/Patch/Delete, JSON validation, expectedRevision та tombstone.

**Приймання:** CAS race має одного переможця для однакової expected revision; duplicate JSON names/oversized/deep payloads відхиляються за контрактом.

### KL-011 · Strict indexes і unique scope

**Етап:** P1. **Залежності:** KL-007, KL-010.

**Робота:** Реалізувати equality/range/composite/partition-unique; old/new index delta в одній транзакції.

**Приймання:** Reference model збігається після insert/update/delete/crash; два live documents не займають один unique key у заявленому scope.

### KL-012 · Batch і persisted idempotency

**Етап:** P1. **Залежності:** KL-009, KL-010, KL-011.

**Робота:** Зберігати CommandId, payload hash, outcome та position атомарно з batch.

**Приймання:** 100 повторів command до/після restart дають один ефект; same ID/different payload дає conflict; retention window задокументовано.

### KL-013 · Query AST, planner і cursors

**Етап:** P1. **Залежності:** KL-004, KL-011.

**Робота:** Реалізувати внутрішні predicates, ordered seek, projection, limits, Explain та versioned cursor; SQL frontend, binding і shared protocol деталізовані у KL-045..KL-054.

**Приймання:** Index plan і reference scan збігаються; pagination stable за контрактом; tampered/expired cursors відхиляються.

### KL-014 · Standalone server і C# SDK

**Етап:** P1. **Залежності:** KL-010, KL-012, KL-013.

**Робота:** HTTP API, CLI, local Orleans host, config/data directory та typed client outcomes.

**Приймання:** Clean machine запускає сервер без зовнішніх сервісів; SDK обробляє unknown write outcome повтором CommandId.

### KL-015 · Security і telemetry baseline

**Етап:** P1. **Залежності:** KL-014.

**Робота:** Додати tenant/auth context, admin boundary, payload/query quotas та OpenTelemetry. До real-data milestone підключити principals, sensitive catalog, usage checks і safe projector із KL-061..KL-064 та ранній admission KL-052.

**Приймання:** Cross-tenant reads/writes/index scans блокуються; invalid payload не доводить сервер до OOM; telemetry не містить payload/секретів.

### KL-016 · Committed projection outbox

**Етап:** P1. **Залежності:** KL-012.

**Робота:** Записувати mutation stream, consumer checkpoints, index generation та retention pins.

**Приймання:** Crash між commit і delivery не втрачає подію; duplicate delivery безпечна; checkpoint не перескакує failed event.

### KL-017 · Consensus provider qualification

**Етап:** P2. **Залежності:** KL-002, KL-003, KL-006.

**Робота:** Оцінити .NEXT persistent-state/flush/term/vote semantics і альтернативний шлях за потреби.

**Приймання:** Є evidence для follower durable-before-ACK та stable term/vote; total power-reset test визначає supported durability; слабший режим має інше explicit ім’я.

### KL-018 · Metadata bootstrap

**Етап:** P2. **Залежності:** KL-017.

**Робота:** Реалізувати seeds, node identity, durable catalog group і прямий internal transport.

**Приймання:** Кластер стартує без Orleans calls до readiness; повторний start не створює інший cluster identity; metadata CAS працює після leader loss.

### KL-019 · Orleans membership і ownership adapter

**Етап:** P2. **Залежності:** KL-018.

**Робота:** Додати membership CAS, partition routing/placement map та explicit epoch fences.

**Приймання:** Duplicate activation і старий epoch не допускають canonical write; restart зберігає filesystem ownership.

### KL-020 · Три-вузловий replicated apply

**Етап:** P2. **Залежності:** KL-009, KL-012, KL-017, KL-019.

**Робота:** Перенести ordered command path на consensus group та atomic lastApplied.

**Приймання:** Після leader loss зберігаються acknowledged durable commands; retries не дублюють effects; minority не приймає strong writes.

### KL-021 · Strong read і session tokens

**Етап:** P2. **Залежності:** KL-020.

**Робота:** Read barrier, applied position, follower stale mode, token validation.

**Приймання:** Read-after-write із token проходить після failover; stale leader не повертає strong read без authority; invalid incarnation відхиляється.

### KL-022 · Graph storage

**Етап:** P3. **Залежності:** KL-011, KL-012.

**Робота:** Entity references, edge record, incoming/outgoing indexes і delete policy.

**Приймання:** Random graph reference збігається; local edge mutation атомарна; duplicate edge command не створює зайвих ребер.

### KL-023 · Bounded graph traversal

**Етап:** P3. **Залежності:** KL-013, KL-022.

**Робота:** Neighbors, BFS, unweighted shortest path, batched frontier і budgets.

**Приймання:** Cycles не зациклюють query; reference shortest path збігається; depth/node/edge/deadline caps реально припиняють виконання.

### KL-024 · Time-series core

**Етап:** P3. **Залежності:** KL-007, KL-012.

**Робота:** Series metadata, AppendBatch, UTC keys, equal timestamps і event dedup.

**Приймання:** Out-of-order/equal-timestamp samples читаються у визначеному порядку; retry не змінює кількість samples.

### KL-025 · Time-series range і aggregates

**Етап:** P3. **Залежності:** KL-013, KL-024.

**Робота:** Range/latest, sum/count/min/max/avg, windows і explicit boundary rules.

**Приймання:** Агрегати збігаються з reference model для boundary/timezone/empty/late data; scanned-record budget дотримано.

### KL-026 · Retention, expiry і rollups

**Етап:** P3. **Залежності:** KL-016, KL-024, KL-025.

**Робота:** Logged expiration, bucket lifecycle, late-event policy, correction і reader leases.

**Приймання:** Expired data не повертається; replay однаковий між вузлами; bucket drop очікує reader leases; avg rollup відновлюється із sum/count.

### KL-027 · Canonical vectors і exact search

**Етап:** P4. **Залежності:** KL-010, KL-016.

**Робота:** Typed sidecar vector fields, dimension/model validation, SIMD scoring та exact allowlist path.

**Приймання:** Distance/top-k збігаються з reference implementation у допустимій похибці; model/dimension mismatch відхиляється; vector update атомарний із revision.

### KL-028 · Text index provider audit

**Етап:** P4. **Залежності:** KL-006, KL-016.

**Робота:** Перевірити ZoneTree.FullTextSearch ranking; оцінити Lucene.NET BM25, analyzers та formats.

**Приймання:** Capability matrix має підтвердження в API/tests; Ukrainian/English corpus пройдено; обраний provider підтримує потрібну replay/delete semantics.

### KL-029 · Text projection і rebuild

**Етап:** P4. **Залежності:** KL-016, KL-028.

**Робота:** Index worker, checkpoint, deletes, analyzer generations, initial maintenance rebuild.

**Приймання:** Видалення проєкції та replay повертають очікувану видачу; crash на checkpoint не приховує missing updates; stale revision не воскресає.

### KL-030 · ANN provider qualification

**Етап:** P4. **Залежності:** KL-006, KL-027.

**Робота:** Managed ANN candidate: source/license/transitive-dependency audit, filtering, concurrency, ownership і save/load. USearch допускається як optional external comparison; обраний baseline deployment зберігає managed-first constraint.

**Приймання:** Exact comparison і stress delete/reinsert пройдено; memory bounded; усі потрібні operations доступні на pinned targets. Деталізований implementation gate продовжується у KL-059.

### KL-031 · ANN projection lifecycle

**Етап:** P4. **Залежності:** KL-016, KL-030.

**Робота:** Snapshot+deltas, atomic generation manifest, replay-safe update/delete та rebuild.

**Приймання:** Crash у всіх manifest/save/replay точках дає коректний recovery; duplicate outbox delivery безпечна; obsolete generation не приймає updates.

### KL-032 · Filtered vector planner

**Етап:** P4. **Залежності:** KL-013, KL-023, KL-027, KL-031.

**Робота:** Allowlist planning, exact fallback, ANN expansion, authoritative final filtering.

**Приймання:** Zero cross-tenant/ACL leaks; recall@10 ≥0.95 на затверджених cohorts або release-blocking result; підсумкова кількість/partial status правдиві.

### KL-033 · Hybrid rank і Explain

**Етап:** P4. **Залежності:** KL-029, KL-032.

**Робота:** BM25/vector candidate merge, weighted RRF, stable ties, optional rerank hook.

**Приймання:** Ranking reproduces deterministic fixture; score contributions пояснюються; candidate-window effects виміряні на relevance corpus.

### KL-034 · Search freshness contract

**Етап:** P4. **Залежності:** KL-016, KL-021, KL-029, KL-031.

**Робота:** CommitToken, WaitForIndex, error states, GC pins та generation handling.

**Приймання:** Wait завершується лише після потрібного prefix; timeout/failure visible; deleted documents відсутні навіть зі stale projection.

### KL-035 · Replica snapshot і installation

**Етап:** P5. **Залежності:** KL-005, KL-020.

**Робота:** Створити committed cut, index/term/config metadata, checksummed transfer та atomic install.

**Приймання:** Follower із порожнім диском наздоганяє leader; interrupted install відновлюється; log GC не видаляє останній recovery path.

### KL-036 · Controlled partition movement

**Етап:** P5. **Залежності:** KL-019, KL-035.

**Робота:** Durable migration state machine: copy/catch-up/barrier/switch/cleanup.

**Приймання:** Crash на кожному кроці без втрати durable commands; старий owner fenced; routing tokens коректно обробляють epoch change.

### KL-037 · Distributed query execution

**Етап:** P5. **Залежності:** KL-013, KL-021, KL-033, KL-036.

**Робота:** Bounded fan-out, per-modality global candidate merge, query cancellation та statistics epoch.

**Приймання:** Порівняння з centralized oracle; exact/approximate differences задокументовано; slow shard дає bounded timeout/explicit partial.

### KL-038 · Cross-partition graph edges

**Етап:** P5. **Залежності:** KL-016, KL-022, KL-023, KL-036.

**Робота:** Canonical edge owner, async reverse adjacency, tombstones та repair.

**Приймання:** Duplicate/out-of-order deliveries не воскресають edges; declared eventual window виміряно; cascade errors видимі.

### KL-039 · Online index generation swap

**Етап:** P5. **Залежності:** KL-004, KL-016, KL-029, KL-031, KL-036.

**Робота:** Consistent cut, delta catch-up, validation, catalog swap та leased retirement.

**Приймання:** Concurrent update/delete під час build не губляться; rollback/restart зберігає active generation; старі readers завершуються без missing files.

### KL-040 · Resource governor

**Етап:** P6. **Залежності:** KL-006, KL-015, KL-020, KL-031.

**Робота:** Розширити ранні node/tenant budgets із KL-052: compaction quotas, hot-partition throttling, memory/disk accounting і lag admission. Базовий admission впроваджується у P1.

**Приймання:** Sustained overload дає bounded memory/queue; disk-full не створює false durable ACK; RSS/native budgets виміряні під rebuild.

### KL-041 · Chaos і corruption suite

**Етап:** P6. **Залежності:** KL-020, KL-035, KL-036, KL-039.

**Робота:** Network partitions, pauses, stale leaders, partial I/O, corrupt WAL, power-reset fault model.

**Приймання:** History checker не знаходить forbidden outcomes; кожна corruption class має явний recovery/fail-stop результат та збережений seed.

### KL-042 · Cluster backup і restore drills

**Етап:** P6. **Залежності:** KL-005, KL-035, KL-038.

**Робота:** Catalog-consistent manifest з per-partition cuts, off-node destination та restore CLI.

**Приймання:** Restore на чистий cluster відновлює declared cut semantics; retained outbox/graph repair узгоджені; RPO/RTO виміряні.

### KL-043 · Upgrade і format compatibility

**Етап:** P6. **Залежності:** KL-001, KL-007, KL-035, KL-039.

**Робота:** Golden databases, rolling upgrade matrix, reader/writer capability negotiation і migrations.

**Приймання:** Попередні supported formats читаються; unsupported downgrade відхиляється до mutation; restore/replay перевірені для кожної supported version pair.

### KL-044 · Release gate і operational handbook

**Етап:** P6. **Залежності:** KL-040, KL-041, KL-042, KL-043.

**Робота:** 72-hour sustained workload, recovery drills, alerts, capacity guide та documented guarantees.

**Приймання:** Немає необмеженого WAL/outbox/compaction backlog; correctness перевірено після тесту; опубліковані raw metrics, fault model і відомі обмеження.

## 18. Організація solution

Початкові assembly boundaries варто тримати компактними. Models усередині Core можуть бути окремими namespaces до стабілізації контрактів.

```text
src/
  KeyLoad.Abstractions/
  KeyLoad.Core/
    Commands/
    Documents/
    Indexes/
    Graph/
    TimeSeries/
    Events/
    Messaging/
      Queues/
      Subscriptions/
      Scheduling/
      Inbox/
    Query/
    Projections/
  KeyLoad.Storage.ZoneTree/
  KeyLoad.Query/
    Syntax/
    Binding/
    Planning/
    Execution/
  KeyLoad.Security/
    Principals/
    Policies/
    Classification/
    Projection/
  KeyLoad.Orleans/
  KeyLoad.Replication/
  KeyLoad.Search/
    Text/
    Vector/
    Hybrid/
  KeyLoad.Server/
  KeyLoad.Client/
  KeyLoad.Cli/
tests/
  KeyLoad.UnitTests/
  KeyLoad.PropertyTests/
  KeyLoad.IntegrationTests/
  KeyLoad.RecoveryTests/
  KeyLoad.ChaosTests/
  KeyLoad.CompatibilityTests/
benchmarks/
docs/
  adr/
  contracts/
  operations/
```

Storage interfaces мають описувати потрібні гарантії, ownership і lifetimes. Вони не експортують ZoneTree types у public SDK. Provider capability містить визначені semantics; виконання fallback зі слабшими гарантіями вимагає explicit configuration.

### ADR, які потрібно прийняти до великого implementation

ADR-001: partition identity та affinity. ADR-002: command format і idempotency. ADR-003: durability/ACK barrier. ADR-004: transactional read views. ADR-005: canonical namespaces/keycodec. ADR-006: synchronous/derived indexes. ADR-007: replica consensus/metadata bootstrap. ADR-008: backup і log retention. ADR-009: text/vector providers. ADR-010: query budgets/security. ADR-011: format upgrade policy. ADR-012..ADR-022 із редакції 0.2 наведені у розділі 36; eventing ADR-023..ADR-031 додані у розділі 46.

## 19. Робота AI-агентів

Кожен агент отримує один bounded ticket, залежні ADR, source version, acceptance cases та дозволені модулі. У PR зазначаються зміни контрактів, tests, allocations/performance impact і failure behavior.

Паралелізація придатна для незалежних modules і test harness після погодження interface contracts. Canonical write path, consensus integration, transaction visibility, file lifecycle та format migrations потребують одного owner і незалежного review.

Заборонено закривати acceptance за mock-only happy path там, де задача стосується fsync, crash, race або recovery. Тестовий harness має вбивати справжній process і перевіряти дані після нового start. Позначка DurabilityPassed додається лише з evidence для оголошеного fault model.

## 20. Остаточні рекомендації

Обрати ZoneTree основним storage-кандидатом і почати з KL-001..KL-008, SQL contracts KL-045..KL-046 та PostgreSQL baseline KL-073. Паралельно описати детермінований command envelope і провести consensus qualification. Перший demonstrator розширюється document + strict index + event append + local enqueue + SQL + permissions/masking + crash/restore. Producer і consumer flows мають спільний TransactionDomain; порядок ранніх eventing tasks уточнює розділ 45.4.

Після стабілізації цього ядра підключити ранній три-вузловий apply spike. Graph/time-series використовують canonical namespaces і загальні budgets. Search використовує transactional outbox, exact ground truth та rebuildable index generations.

Продуктова гіпотеза KeyLoad: спільні API/routing/security та самостійний запуск для документів, зв’язків, Event Store, queues і search; co-located atomic processing та прозорі freshness/delivery contracts. Продуктивність і операційну перевагу перевіряють benchmark gates.


## 21. Рішення версії 0.2 і пояснення компонентів

Це розширення уточнює попередні розділи. Номери KL-001..KL-044 збережені; нові задачі починаються з KL-045. Версія 0.2 позначає редакцію дизайну. Пакет KeyLoad із таким API ще належить реалізувати.

### 21.1. Платформа, Orleans і .NEXT

`.NET` є платформою виконання та бібліотек для C#-застосунків. `Microsoft Orleans` додає virtual actors, адресацію grains, повідомлення і життєвий цикл activations. `.NEXT` є окремим набором бібліотек, серед яких є інструменти Raft. Назви .NET та .NEXT у попередньому обговоренні легко переплутати. Підключення .NEXT саме по собі не створює готову розподілену базу. [S18][S45][S13][S14]

У вислові про «Erlang, який хостить клієнтів» цей дизайн продовжує початкову ідею з Microsoft Orleans. Erlang/BEAM залишається окремою технологією, яка не входить у цільовий стек KeyLoad. Мережеві з'єднання приймає ASP.NET Core; Orleans виконує внутрішні actor-операції.

Базова конфігурація KeyLoad має бути managed-first: storage, query planner, permissions, graph і пошукова логіка на C#/.NET. Для ANN пріоритет має managed implementation, яка проходить окрему перевірку якості та concurrency. USearch із C++ ядром зберігається у дослідній матриці як зовнішній еталон. Включення native index/ML бібліотеки в дистрибутив потребуватиме окремого рішення. У KL-001 перевіряються також transitive dependencies і compression providers; managed-first не є вже проведеним аудитом усіх пакетів. [S11][S12]

### 21.2. Що беремо за основу

Рекомендований перший сервер складається з ASP.NET Core, Orleans, ZoneTree adapter, власного planner/executor, локального каталогу політик і bounded resource governor. Scalar SQL з'являється разом із першими індексними запитами. Graph, time-series та search operators підключаються до цього самого executor поступово.

Власні частини: модель ідентичності, atomic partition contract, query semantics, security binder, deterministic command apply, shard routing, projection lifecycle та операційні гарантії. Кандидати на повторне використання: SQL parser, Lucene.NET BM25, managed HNSW, Raft toolkit. Кожен кандидат отримує capability tests. Наявність NuGet-пакета не закриває acceptance задачі інтеграції.

Мережевий API першого релізу: HTTP із JSON request/response та потоковими сторінками результатів. Binary vector payload і gRPC вводяться після benchmark transport. Один `.NET SDK` приховує транспорт і реалізує повторення write-команд із тим самим CommandId.

## 22. Сучасні системи, які варто дослідити на власних сценаріях

Нижче наведено релевантні архітектурні приклади з офіційних джерел. Таблиця описує конкретні механізми; оцінювання продуктивності потребує нашого benchmark.

| Система | Підтверджений механізм | Що перевіряти для KeyLoad |
|---|---|---|
| PostgreSQL + pgvector | Scalar indexes, JSON-oriented indexing, full-text; ANN із iterative scans та filter/partition strategies. [S36][S37][S38] | Основний конкурентний baseline, query plans, CPU, RAM, latency, recovery |
| Qdrant | Query API з prefetch, кількома retrieval stages, RRF/DBSF; shared і dedicated tenant placement. [S22][S25] | Candidate planning, filtering, migration великих tenants, multi-stage retrieval |
| Weaviate | Allowlist на основі inverted index, filtered HNSW, ACORN strategy. [S23] | Як поєднати scalar/ACL filters із ANN без різкого падіння recall |
| RavenDB | SQL-подібна RQL, векторний пошук, document-associated time series. [S27][S28][S29] | Зручність .NET SDK, sessions, indexes, diagnostics і наскрізна модель документів |
| SurrealDB | SQL-подібна SurrealQL, graph-oriented запити, search-функції fusion. [S30][S31] | Зрозуміла користувачу мова багатомодельних запитів |
| ArangoDB | AQL traversals і vector index functions. [S32][S33] | Locality графа, traversal pruning, семантика filter placement |
| Vespa | Data-local retrieval і кілька фаз ranking із global reranking. [S24] | Розподіл дешевих та дорогих стадій пошуку між вузлами |
| Neo4j | Cypher SEARCH для vector indexes у graph-запитах; документація розрізняє index filtering і подальший WHERE. [S34] | Graph-scoped ANN, пояснення області кандидатів і вартості traversal |
| Marten + Wolverine | PostgreSQL documents/events і transactional messaging integration. [S54][S55] | Combined document/event/outbox/inbox baseline |
| KurrentDB | Stream expected revision, atomic appends і persistent subscriptions. [S48][S49] | Append/replay/consumer-group semantics |
| RabbitMQ | Publisher confirms, consumer ACK та quorum queues. [S50][S51] | Specialized durable queue benchmark |

Назва Qdrant у цьому документі відповідає векторній базі, згаданій у запиті користувача. Публічний ринковий «квадрант» тут не використовується.

RavenDB потрібно включити до практичного порівняння developer experience: користувач KeyLoad у .NET-екосистемі матиме саме такий близький функціональний орієнтир. PostgreSQL залишається основним інженерним baseline. Qdrant і Weaviate дають спеціалізовані vector/filter baselines; Neo4j або ArangoDB корисні для traversal workload. Повна автоматизована матриця всіх систем має сенс після появи відтворюваного PG baseline.

## 23. Мова запитів: KeyLoad SQL і спільний серверний план

### 23.1. Рішення

Публічна текстова мова: **KeyLoad SQL**, визначена підмножина SQL із розширеннями для JSON, graph, time-series і search. Два інші входи: JSON query envelope та типізований C# builder. Усі входи транслюються в один внутрішній AST та проходять однакові перевірки.

```text
KeyLoad SQL ─── parser ──────────┐
JSON query ─── schema decoder ──┼── Unbound AST
C# builder ─── wire envelope ────┘       │
                                     Binder
                          names + types + capabilities
                              + field-use permissions
                                       │
                            Authorized logical plan
                                       │
                              Cost/rule optimizer
                                       │
                              Physical query plan
                                       │
                            Data-local execution
```

Для прямого Get/Put/Batch SDK використовує компактні команди. Ці операції проходять спільні security та storage contracts. SQL parser у їхній гарячий шлях додавати необов'язково.

SQL-подібні frontends мають практичні аналоги: RavenDB RQL і SurrealQL. Це підтверджує можливість такої форми API; коректність і продуктивність KeyLoad визначатимуть його власні semantics та executor. [S27][S30]

### 23.2. Межі підтримки

| Хвиля | Семантика |
|---|---|
| Q1, разом із scalar core | SELECT, projection, aliases, typed parameters, WHERE, AND/OR/NOT, equality/range/IN, IS NULL, ORDER BY, LIMIT, EXPLAIN |
| Q2 | GROUP BY/агрегати з budget; time windows; graph operators; authorized EVENTS/QUEUE_MESSAGES views |
| Q3 | SEARCH HYBRID, named retrieval branches, RRF, candidate limits, index freshness і ranking diagnostics |
| Q4 | Bounded equijoins, CTEs, prepared statements, query profiles, змістовний набір SQL write statements |
| Окремі майбутні проєкти | Повна PostgreSQL-семантика, PostgreSQL wire protocol, ODBC/JDBC, довільні recursive SQL, глобальні distributed joins і arbitrary UDF |

Підтримка parser-ом певної конструкції закінчується додатковою server-side capability перевіркою. Unsupported function, join або recursive query повертає діагностику до виконання. Автоматичне скачування всієї колекції в клієнт для LINQ evaluation заборонене.

SQL text compatibility, SQL semantics, PostgreSQL network protocol і BI-driver compatibility мають окремі acceptance suites. Перший реліз заявляє тільки явно реалізовану підмножину KeyLoad SQL.

### 23.3. Parser

SqlParser-cs є .NET parser-ом, що створює AST і має hooks для dialect extensions. Його README прямо обмежує відповідальність синтаксичним розбором. Name resolution, type checking, policy enforcement, planner та execution потрібно реалізувати у KeyLoad. [S21]

KL-047 порівнює два підходи: pinned SqlParser-cs із dialect adapter і невеликий власний parser із recursive-descent statements та Pratt expression parsing. Вибір залежить від extension hooks, diagnostics, AST stability, fuzzing і вартості підтримки. До завершення цієї задачі приклади нижче є проєктною граматикою.

### 23.4. Типи та JSON

Для indexed fields schema задає `string`, `int64`, `decimal`, `float64`, `bool`, `timestamp`, `entityRef` чи `vector`. JSON може мати додаткові payload-поля; їхнє використання в запиті підлягає bind/typecheck і scan policy. `c.address.city` резолвиться в структурований field path. Поле з крапкою у власному імені потребує quoted identifier.

`MISSING` означає відсутній JSON path; `NULL` означає присутнє null-значення. Scalar comparison із ними має результат UNKNOWN; WHERE пропускає тільки TRUE. `IS NULL` перевіряє explicit null, `IS MISSING` перевіряє відсутність. `COUNT(*)` рахує дозволені рядки, `COUNT(field)` виключає null/missing. Обидві конструкції проходять field-use authorization. Ці правила є частиною KeyLoad dialect, тому compatibility tests мають перевіряти їх явно.

Implicit string-to-number cast відсутній. Змішані числові типи мають явну promotion policy. `TRY_CAST` повертає documented null outcome; звичайний CAST повертає помилку без відображення sensitive input. Час параметрів нормалізується до UTC; schema зберігає точність. Collation і analyzer versions входять у index generation та plan-cache key. Для однакових user sort keys executor додає повну EntityRef як останній tie-break, оскільки однакові EntityId можливі в різних partitions. Службові _ref/_meta/$score мають reserved namespace і не можуть бути підмінені payload-полями документа.

### 23.5. Приклади проєктної мови

Scalar query Q1:

```sql
SELECT d.id, d.title, d.updatedAt
FROM documents AS d
WHERE d.projectId = @projectId
  AND d.status = 'published'
  AND d.updatedAt >= @since
ORDER BY d.updatedAt DESC, d.id
LIMIT 50;
```

Для цього query planner може використати composite index `(projectId, status, updatedAt DESC, id)`, якщо він існує та сумісний із partitioning. Tenant і principal задаються перевіреним контекстом запиту. Фільтр projectId не дає користувачу прав на чужий проєкт.

Hybrid query Q3, **запропонований синтаксис KeyLoad**, який ще треба реалізувати:

```sql
SELECT c.id, c.title, SCORE() AS score
FROM chunks AS c
WHERE c.projectId = @projectId
  AND c.status = 'published'
  AND GRAPH_REACHABLE(
        @projectRef, c._ref,
        labels => ['contains'], max_depth => 3
      )
SEARCH HYBRID (
  TEXT(c.title, c.body, query => @text),
  VECTOR(c.contentEmbedding, query => @embedding),
  GRAPH(c._ref, seeds => @seedRefs,
        labels => ['related_to'], max_depth => 2)
)
FUSE RRF (rank_constant => 60, weights => [1.0, 1.0, 0.5])
ORDER BY score DESC, c.id
LIMIT 20;
```

`GRAPH_REACHABLE` тут задає обов'язкову область результатів. `GRAPH` у SEARCH створює окремий ранжований список від explicit seeds. Для запиту з двома джерелами третю branch опускаємо та задаємо дві ваги. Значення ваг у прикладі є стартовим experiment configuration.

WHERE у цьому dialect семантично визначає eligibility **до** retrieval top-k. Planner може переставляти фізичні операції тільки зі збереженням цього контракту. LIMIT застосовується до фінальної видачі. Пізній candidate-only filter потребує окремого явного оператора та documented approximation.

Проєктний time-series оператор Q2:

```sql
SELECT TIME_BUCKET(@step, s.timestamp) AS bucket,
       AVG(s.value) AS averageValue,
       MAX(s.value) AS peakValue
FROM TIME_SERIES(@entityRef, 'latency') AS s
WHERE s.timestamp >= @from AND s.timestamp < @to
GROUP BY bucket
ORDER BY bucket;
```

`TIME_SERIES` резолвить entity/series scope і permissions до сканування. Для AVG за rollups передаються sum/count. Параметр step має positive duration type; довільне число buckets обмежується governor.

## 24. Query envelope, C# SDK та життєвий цикл запиту

### 24.1. Єдиний transport contract

Наступний JSON описує той самий тримодальний запит, що й SQL у 23.5. Поля позначають v1 design schema; результат KL-045 зафіксує точні public names.

```json
{
  "languageVersion": 1,
  "collection": "chunks",
  "select": ["id", "title", "$score"],
  "parameters": {
    "projectId": {"type": "string", "value": "project-42"},
    "projectRef": {
      "type": "entityRef",
      "value": {"collection": "projects", "partitionKey": "project-42", "id": "project-42"}
    },
    "text": {"type": "string", "value": "відновлення після збою"},
    "embedding": {"type": "vectorAttachment", "attachmentId": "v1"},
    "seedRefs": {
      "type": "entityRef[]",
      "value": [{"collection": "chunks", "partitionKey": "project-42", "id": "chunk-7"}]
    }
  },
  "where": {
    "and": [
      {"eq": [{"field": "projectId"}, {"parameter": "projectId"}]},
      {"eq": [{"field": "status"}, {"literal": "published"}]}
    ]
  },
  "graphScope": {
    "root": {"parameter": "projectRef"},
    "labels": ["contains"],
    "direction": "outgoing",
    "maxDepth": 3
  },
  "retrieve": [
    {"name": "lexical", "kind": "text", "fields": ["title", "body"], "query": {"parameter": "text"}, "candidateLimit": 200},
    {"name": "semantic", "kind": "vector", "field": "contentEmbedding", "query": {"parameter": "embedding"}, "candidateLimit": 200},
    {"name": "related", "kind": "graph", "seeds": {"parameter": "seedRefs"}, "labels": ["related_to"], "maxDepth": 2, "candidateLimit": 200}
  ],
  "fusion": {"kind": "weightedRrfV1", "rankConstant": 60, "weights": [1.0, 1.0, 0.5]},
  "orderBy": [{"field": "$score", "direction": "desc"}, {"field": "id", "direction": "asc"}],
  "limit": 20,
  "options": {"deadlineMs": 2000, "allowPartial": false, "freshness": "indexed"}
}
```

У SQL-path candidate windows та resource options надходять у тому самому request envelope поряд із SQL text і typed parameters. За однакових параметрів та options SQL і JSON мають дати однаковий bound plan. `candidateLimit=200` і deadline 2000 мс є прикладом, який не встановлює SLO продукту.

Tenant, principal, role grants, policy epoch, storage addresses і внутрішні index ordinals не приймаються як trusted поля клієнтського JSON. EntityRef у запиті знаходиться всередині authenticated tenant; міжtenant-посилання перевіряються окремим адміністративним контрактом.

### 24.2. C# interface

Проєктний приклад використання SDK:

```csharp
// Ескіз API KeyLoad, який підлягає реалізації.
var result = await client.QueryAsync<SearchHit>(
    sql: queryText,
    parameters: new QueryParameters()
        .Add("projectId", projectId)
        .Add("projectRef", projectRef)
        .Add("text", searchText)
        .AddVector("embedding", embedding, vectorSpaceId)
        .Add("seedRefs", seedRefs),
    options: queryOptions,
    cancellationToken: cancellationToken);
```

Builder з lambda підтримує whitelist field access і операторів, які можна серіалізувати в AST. Довільний C# delegate не виконується в сервері. Parser/types dependency не витікає у публічні DTO. Помилки `UnsupportedExpression`, `PermissionDenied`, `IndexNotReady`, `BudgetExceeded`, `TokenInvalidated` та `UnknownWriteOutcome` мають стабільні error codes.

### 24.3. Clients і grains

`ClientId` або `SessionId` може адресувати `SessionGrain`, який зберігає маленькі session tokens, subscription state і cursor references. Зберігати там кожен документ чи всі результати клієнта без квот заборонено. Для звичайного stateless query session grain може взагалі не активуватися.

Orleans activation виконує один turn за раз. Типово запит утримує non-reentrant activation до завершення; await сам по собі не відкриває її для наступного request. Reentrancy дозволяє interleaving turns. CPU-паралельність усередині однієї activation цим не створюється. [S18][S19]

Тому session actor швидко повертає immutable request context або token. Пошук виконує незалежний QueryExecutionContext, локальний coordinator service або окремий QueryJobGrain для довготривалої керованої задачі. Один повільний запит не повинен затримувати всі запити цього клієнта за спільною session-чергою.

StatelessWorker grains утворюють pool локальних activations. Їхнє розміщення саме по собі не задає потрібну physical replica даних. Replica-local виконання потребує explicit routing до відповідного вузла та звернення до його PartitionHost. [S20]

### 24.4. Response і cursor

Response містить queryId, items, revisions, search/index watermarks, generation IDs, routing epoch, policy epoch, completeness, approximation flags і redaction metadata. Detailed score contributions доступні окремим diagnostic permission. Payload-секрети у plan/trace відсутні.

Cursor зберігає opaque resume state із tie-break keys, principal/tenant binding, policy/schema/index generation, expiry та MAC. Перенесення cursor між principals відхиляється. SQL OFFSET може бути доступним тільки в межах малого declared budget. Великі results обходяться keyset pagination чи leased query cursor.

При зміні політики cursor повторно авторизується або інвалідується. Кожна нова streaming page має auth checkpoint. Bytes, які вже пішли клієнту до відкликання дозволу, повернути назад неможливо; контракт revocation чітко визначає межу наступного request/page.

## 25. Planner та executor: як реально виконується запит

### 25.1. Мінімальні внутрішні інтерфейси

Внутрішня модель розділяє `UnboundQuery`, `BoundQuery`, `AuthorizedLogicalPlan`, `PhysicalPlan`, `QueryFragment`, `RecordBatch` та `QueryResult`. Ці типи належать KeyLoad і мають версійовані contracts.

`IQueryBinder` прив'язує names/paths до catalog field IDs. `IQueryAuthorizer` перевіряє використання полів і додає trusted row policy. `IQueryPlanner` вибирає операції. `IShardQueryExecutor` виконує fragment над leased committed view. `IResultProjector` застосовує дозволену projection і masking. `IQueryGovernor` контролює фактичні витрати.

`Candidate` містить EntityRef, документну revision, branch ID, native score і generation/position джерела. Shard-local ordinal допустимий тільки всередині fragment та відповідної generation. Зовнішній результат і merge між shards використовують повну EntityRef.

### 25.2. Набір фізичних операторів

| Оператор | Призначення |
|---|---|
| PointLookup / MultiGet | Прямий доступ за ключем і пакетне читання |
| IndexSeek / IndexRangeScan | Equality, composite prefix, ordered range |
| BitmapAnd / BitmapOr / SortedIdIntersect | Перетин дозволених IDs і scalar predicates |
| ResidualFilter | Перевірка дозволених предикатів, не покритих індексом |
| GraphExpand / GraphSemiJoin | Batched frontier і перевірка graph scope |
| TextTopK | Retrieval за текстовим індексом |
| ExactVectorTopK / AnnTopK | Exact або approximate vector candidates |
| BranchMerge / RankFusion | Міжshard-об'єднання branch і fusion |
| PartialAggregate / FinalAggregate | Локальні агрегати та їхнє злиття |
| FetchDocuments / SecureProject | Пізня materialization, ACL, masking |
| Exchange / TopKMerge | Передавання bounded batches і глобальний порядок |

На старті використовуємо rule-based planner: point lookup → найселективніший доступний scalar index → решта residual predicates → bounded sort/aggregate. Cardinality estimates, histogram/sketch statistics і alternative plans додаються поступово. Статистика включає security-domain scope та version.

### 25.3. Security barriers в оптимізації

Policy operators входять у план до оптимізації. Переписування AST не повинно виносити user expression, UDF, sorting або searchable field за межу авторизації. Поля schema мають окремі capabilities для output, predicates, sorting, grouping, text/vector retrieval та raw read.

Навіть `EXPLAIN` перевіряє права на collection, index та field names. Детальні cardinalities, distances, graph paths і policy details повертаються тільки в дозволеному diagnostic scope. Параметри з classification `sensitive` замінюються placeholder-ами у всіх serialized plans.

### 25.4. Read views та parallel execution

Для bounded scalar scans початкове ядро може використовувати read/apply gate із розділу 5. Це обмежує паралельність на physical shard. Такий варіант має явний статус safe baseline; production scalability claim потребує перевірених immutable committed views або власної versioned read-view реалізації.

Перед запуском паралельних branches coordinator отримує catalog/policy context і read-view descriptors. Короткий canonical read не змішує половини transaction batch. Search projections можуть відставати, тому search plan має окремий freshness contract.

Кожен fragment виконується на вузлі, який має відповідну replica та її readiness. QueryCoordinator звертається до того самого committed cut у межах заявлених гарантій. Cross-shard read повертає vector of cuts. Глобальний timestamp у response не перетворює цей набір на globally serializable snapshot.

### 25.5. Що означає багатопоточність

Записи одного physical shard отримують визначений порядок у writer queue/replicated log. Independent shards застосовують записи паралельно. Read-only computations над стабільними views можуть паралелитися через worker pools.

Основні способи паралельності: independent client requests; independent shards; незалежні text/vector branches; кілька CPU blocks exact-vector scan; окремі background maintenance jobs. Для кожного є спільний node-level budget. Створення Task для кожного документа, ребра або vector distance заборонене.

`Task.Run` сам по собі не гарантує isolation. Heavy scoring отримує admission semaphore/queue та bounded concurrency. Consensus/control work має окремий бюджет і пріоритет. Код, винесений із grain scheduler, отримує immutable inputs і працює через thread-safe storage adapter; доступ до mutable grain state з цього коду заборонений.

### 25.6. Explain/Profile

`EXPLAIN` показує route pruning, index selection, estimates, ranking profile, candidate windows та memory budget. `PROFILE` додає фактичні counts/time для operators. У normalized report зберігаються queue wait, read barrier, scalar filtering, graph traversal, retrieval, merge, materialization і projection lag.

`EXPLAIN` не запускає дорогого query. `PROFILE` запускає його з тими самими permission та quota правилами. Будь-які оціночні цифри позначаються як estimates. Доступ до raw parameter values для sensitive fields відсутній навіть у debug-mode за замовчуванням.

## 26. Текст + вектори + граф: спільна пошукова семантика

### 26.1. Результати належать спільній моделі

Text, vector і graph retrieval повинні повертати сумісні сутності. Для пошуку chunks усі branches повертають ChunkRef. Graph nodes типу Project чи Company можуть бути intermediate vertices; їхній результат явно проектується на chunks через дозволений traversal.

Перед fusion усуваються дублікати EntityRef у кожній branch. Кілька шляхів до одного chunk не множать його внесок автоматично. Кілька embeddings одного документа мають explicit aggregation policy, наприклад max score. Після chunk search опційний `GroupByDocument` обмежує кількість chunks на документ; якість групування оцінюється окремо.

### 26.2. Три ролі графа

**GraphScope.** Обов'язкова умова: результат досяжний із root через задані labels і максимальну depth. Спочатку можна побудувати allowed-ID set, а потім виконати retrieval у ньому. Для великої області planner може застосувати alternative semi-join strategy, зберігаючи declared completeness.

**GraphRetriever.** Explicit seeds породжують третій список кандидатів. Стартовий score: `1/(1 + shortestHops)` з deterministic tie-break EntityRef, depth cap і без path-count amplification. Ваги ребер, Personalized PageRank і random-walk methods є окремими майбутніми profiles. Seeds надходять від клієнта або окремої явно описаної entity-resolution стадії.

**GraphExpansion.** Від top search hits добирається пов'язаний контекст, наприклад документ, проєкт, залежні інциденти. Таке розширення має власні limits і output section. Відсутній у первинних candidate windows документ може залишитися невиявленим; цей режим має candidate-limited recall contract.

GraphScope і GraphRetriever можна використати одночасно, як у прикладі SQL. GraphExpansion часто доречний для побудови відповіді AI-агента після retrieval.

### 26.3. Traversal semantics

ACL на vertex/edge перевіряється під час traversal. Початкова безпечна політика: перехід через недозволений vertex/edge припиняється. Транзит через приховані вершини потребує окремого `graph.traverseHidden` contract, який не входить у minimal release.

Scalar filter на endpoint, наприклад `chunk.status='published'`, не повинен автоматично відкидати intermediate vertex типу Project, який не має поля status. Predicate pushdown класифікується як endpoint-only, edge condition або traversal prune. Офіційний AQL окремо описує traversal та pruning, що корисно як reference для semantic tests. [S33]

Cross-partition reverse adjacency має eventual contract із розділу 8. Запит через таку adjacency повідомляє її watermark і completeness. Обіцянка strongly complete graph scope до catch-up відсутня. Для вищих гарантій потрібен додатковий протокол узгодженого traversal cut.

### 26.4. Рекомендований pipeline

```text
Authenticate → current policy epoch → bind + field-use authorization
                               │
                  Tenant / row ACL / live IDs
                               │
                Scalar filter + optional GraphScope
                               │
                     Eligible endpoint set
                               │
       ┌───────────────────────┼────────────────────────┐
       │                       │                        │
  Text branch             Vector branch            Graph branch
  BM25 candidates         exact / filtered ANN     seeded traversal
       │                       │                        │
  Merge across shards     Merge across shards      Merge across shards
       └───────────────────────┼────────────────────────┘
                               │
                 Validate candidate revisions / ACL
                               │
                  Refill + rank each complete window
                               │
                      Weighted RRF / Top-K
                               │
                 Optional authorized reranker
                               │
              Current output policy → safe projection
```

Candidate validation до fusion означає, що застарілі або недозволені entries не повинні займати ранги у фінальних branch lists. Незмінені vector/text payloads можуть оновлювати revision metadata без повної перебудови індексного графа; цей шлях усе одно проходить committed outbox і checkpoint. Якщо revalidation після materialization видаляє hit, виконуємо bounded refill та перерахунок ranking. Коли refill budget вичерпано, response явно повідомляє short/partial outcome відповідно до request contract.

### 26.5. Weighted RRF v1

Власний versioned profile KeyLoad:

`RRF(d) = Σ_i [d ∈ branch_i] × w_i / (c + rank_i(d))`.

Ранги починаються з 1, `c > 0`, weights невід'ємні, щонайменше одна вага додатна. Відсутність hit у branch дає нульовий внесок. `c=60` і weights із SQL прикладу є experiment defaults. Власні raw BM25 scores, cosine similarities і hop counts мають різні шкали; RankFusion працює з rank positions.

Приклад обчислення з c=60 та weights 1,1,0.5: кандидат A має ranks 1,10,2 і отримує `1/61 + 1/70 + 0.5/62 ≈ 0.0387437`. Кандидат B має ranks 4,2 та відсутній у graph branch: `1/64 + 1/62 ≈ 0.0317540`. Це арифметична демонстрація profile, яка не вимірює relevance.

У сучасній документації Qdrant є власні rank convention, constant і weighted formula; default values різних систем відрізняються. KeyLoad зберігає `weightedRrfV1` і свої golden fixtures, тому оновлення стороннього provider не повинно мовчки змінювати ranking. [S22]

Candidate windows впливають на результат. Документ, відсутній у всіх отриманих windows, не може потрапити у fusion. За finite windows результат точний щодо цих списків. Exhaustive global top-k за RRF потребує ширших retrieval guarantees або bound-based алгоритму, який окремо доводиться.

### 26.6. Distributed ranking

Для кожної modality збираємо candidate lists з усіх релевантних shards, застосовуємо єдиний total order, формуємо global modality window, потім виконуємо fusion. RRF локальних shard ranks може змінити результат після простого reshaping даних, тому таке ранжування не є default contract.

Для exact retrieval локальний top-L з кожного shard достатній для global top-L, якщо всі shards використовують порівнянні scores, той самий cut/eligibility і deterministic tie-break. ANN додає власний approximation error. Навіть exact global top-L по кожній modality не дає автоматичної exhaustive RRF гарантії за межами candidate union.

BM25 потребує визначеного corpus/statistics epoch. У strict ranking profile term statistics агрегуються в межах tenant/security corpus. Локальні shard statistics допускаються в окремому швидкому profile з declared approximation. RRF між text і vector не виправляє непорівнянність text scores між shards.

Вартість exact statistics під довільним per-user ACL може бути значною. Minimal security contract ізолює статистику між tenants та явно визначає corpus усередині tenant. Exact noninterference scores за довільними overlapping ACL не заявляється; для вимоги такого рівня застосовуються окремі security-domain indexes, приховані diagnostics і спеціальний аудит. Shared ANN topology теж може впливати на timing/recall.

### 26.7. Freshness, re-ranking і embeddings

`indexed` читає доступні projections і повертає їхні watermarks. `atLeastToken` чекає contiguous applied prefix потрібних branches. Token не задає єдиного globally atomic search snapshot і не усуває ANN approximation. Під час revalidation candidate revision звіряється з current authoritative document. Застарілий score від зміненого документа відхиляється або потрапляє тільки в окремий explicit stale-ranking profile.

Embeddings у першій версії розраховує клієнт чи opt-in job. У запит передається typed vector і vectorSpaceId. Inside transaction apply зовнішні inference calls відсутні. Model upgrades створюють новий vector field/index generation та контролюють dual-write/rebuild window.

Reranker отримує виключно дозволені fields із safe projection. Передавання raw sensitive text до зовнішнього model provider вимагає спеціального permission та конфігурації. Privacy classification поширюється на derived embeddings, summaries і stored snippets. Вимкнення output поля після того, як воно вже потрапило в embedding corpus, потребує вилучення/перебудови відповідних derived representations.

## 27. Алгоритми індексації й пошуку

### 27.1. Scalar indexes та sets

Ordered index має key `(atomicPartition, indexGeneration, typedValues, EntityId)`. Composite prefix дозволяє range seek. Для малих result sets використовуються sorted ID arrays та merge intersections. Для великих sets оцінюємо dense bitmaps і managed Roaring-compatible representation. Прийняття конкретної бібліотеки залежить від license/format/concurrency audit.

Shard-local compact ordinal може зменшити пам'ять ID sets. Mapping ordinal↔EntityRef є versioned. Після delete, rebuild або migration попередній ordinal не повинен адресувати інший документ у старому cursor/bitmap. Безпечні рішення: generation-scoped ordinals або monotonic allocation із tombstones.

### 27.2. Text index

MVP: BM25 із provider adapter, field boosts, explicit analyzer versions та committed projection lifecycle. Для Lucene.NET доступність BM25 підтверджена API. Для ZoneTree.FullTextSearch перевірка ranking залишається окремою задачею. [S9][S10]

Важливі результати читання README ZoneTree.FullTextSearch: `DeleteRecord` без secondary index може вимагати повного scan; API cancellation може повертати вже зібрану частину результатів. Adapter мусить правильно позначати partial outcome. Dual-tree індексація має пройти recovery tests. [S9]

Власний optimized text index надалі може використовувати segmented postings, term dictionary, positions, skip blocks, WAND/Block-Max WAND. До реалізації цих алгоритмів потрібні exact scoring fixtures і conservative upper-bound tests, щоб pruning не відкидав допустимий top-k. Hash-only token key потребує перевірки collision semantics; строгий режим може зберігати token bytes або collision-verification dictionary.

### 27.3. Vector index

Exact search із C allowed vectors розмірності d виконує приблизно Θ(C×d) арифметичної роботи. Layout: contiguous numeric blocks, explicit dimensions, metric, optional normalized vectors, bounded SIMD loops, top-k heap. Read path уникає JSON parsing embeddings і allocations на кожну distance.

Для ANN розглядаємо managed HNSW: рівні proximity graph, параметри connectivity, construction effort і search effort, deterministic serialized format та контроль rebuild. HNSW є окремим індексним графом над vectors; property graph KeyLoad має власні vertices/edges і business semantics. Початкова стаття HNSW є reference для алгоритму, а actual implementation мусить пройти наші tests. [S42]

Параметри HNSW не фіксуємо універсально. Benchmark підбирає їх за recall/latency/memory, dimensions, filter selectivity та updates. Arbitrary deletion, concurrent search/update і persisted graph recovery є release gates. Робочий старт: immutable index generation із mutable delta та tombstones; alternative concurrent mutable graph приймається тільки після stress tests.

### 27.4. Filtered ANN

Planner оцінює allowed cardinality, filter/vector correlation, candidate budget і index capabilities. Малий allowed set отримує exact scoring. Великий set використовує filtered ANN. За недостатньої видачі збільшуються retrieval effort/window до ліміту або застосовується exact fallback у дозволеному бюджеті.

Технічний нюанс: HNSW traversal іноді потребує проходу через nodes, які не можуть бути output candidates. Наївне припинення навігації на кожній відфільтрованій вершині може зруйнувати доступність частини графа. Weaviate описує allowlist-aware traversal і ACORN strategy; ACORN paper досліджує predicate-aware ANN. [S23][S43]

Це internal ANN navigation; дозволи на traversal business graph регулюються окремо. Shared ANN між tenants не дає автоматичного timing noninterference. Високочутливі workloads мають окремі indexes/security domains.

### 27.5. Business graph

Adjacency blocks групуються за source, direction і label. BFS frontier пакетується за shard і читається одним RPC на пакет. Зберігається visited set, найкоротша depth і bounded predecessor state, якщо потрібен path. Послідовні dependency rounds обмежені traversal depth; додавання CPU не прибирає їхню network latency.

High-degree nodes отримують segmented adjacency та continuation tokens. Supernode не завантажується повністю в один List. Weighted shortest path, PPR і centrality indexes мають окремі contracts та performance gates; online global PageRank не входить у simple query path.

Для graph-aware ANN подальше дослідження може використати NaviX як приклад adaptive execution з дозволеними IDs. Це науковий reference; сумісного C# production-компонента в цьому дослідженні не кваліфіковано. [S44]

### 27.6. Time-series chunks

Після базового ordered sample storage випробовуємо time-window chunks із columnar timestamp/value blocks. Candidate codecs: delta/delta-of-delta для timestamp, lossless XOR або typed delta encoding для numeric samples. Перехід на codec залежить від власного workload і lossless roundtrip/property tests.

Open chunk приймає append; sealed chunks мають immutable manifest. Late samples потрапляють у bounded correction chunk. Background merge створює нову generation. Retention і rollups працюють по window metadata та зберігають last processed position. Scalar tag index дозволяє pruning серій до читання samples.

## 28. Шардінг, репліки та масштабування

### 28.1. Словник фізичних і логічних одиниць

У попередніх розділах слово partition використовувалося для atomic scope і storage ownership. Реалізація має закріпити точні назви:

| Термін | Значення |
|---|---|
| Tenant | Межа ізоляції та квот організації |
| Affinity key | Бізнес-групування, наприклад projectId |
| TransactionDomainId | Catalog binding resources для спільного atomic scope, див. 41.1 |
| AtomicPartitionId | Незмінна логічна межа transactional batch і partition-unique |
| Virtual bucket | Routing unit, що містить цілі atomic partitions |
| Physical shard | Owner unit із власним ordered apply та фізичними engine/index handles |
| Replica group | Leader/followers одного physical shard і його consensus log |
| Node | Процес сервера із persistent NodeId, CPU/RAM/disks |
| Orleans activation | Живий actor execution context із власним lifecycle |

Початковий прототип може мати 1 atomic partition : 1 physical shard. Для entity-scoped domains, наприклад orderId, packing багатьох partitions у physical shard закладається до масштабного ingestion. Для багатьох дрібних tenants допускається packing кількох atomic partitions у один shard. Public batch contract залишається atomic-partition-scoped, навіть коли records фізично знаходяться в одному engine. Це дозволяє майбутню міграцію без зміни гарантій API.

### 28.2. Routing

```text
Tenant + database + catalog resource binding + partition key
                         │
          Resolve TransactionDomain → AtomicPartitionId
                         │
             Stable hash / declared range scheme
                         │
                    Virtual bucket
                         │
          Versioned routing map from catalog consensus
                         │
             Physical shard → eligible replica node
```

Stable hashing має versioned algorithm і golden vectors. `GetHashCode()` runtime-об'єкта не є persistent routing format. Додавання вузла змінює placement map через durable migration protocol. Прямий `hash % liveNodeCount` не використовується як authoritative key-to-data mapping.

Проєктний приклад: 4096 logical buckets, упакованих у 32 physical shards. Ці числа демонструють indirection; initial deployment підбирає їх за engine overhead і workload. Дрібний tenant займає частину shard, великий може отримати dedicated shards. Аналог shared/dedicated tenant placement описано у Qdrant. [S25]

### 28.3. Affinity і гарячі partition

Для corporate knowledge default affinity може бути projectId. Сутності, що часто обходяться разом, співрозміщуються. Для дуже великого проєкту schema заздалегідь оголошує кілька atomic partitions, наприклад `(projectId, bucketOfDocumentId)`. Усі project-wide запити тоді є fan-out queries, а atomic batch діє всередині вибраного partition.

Moving replica, splitting physical shard і splitting atomic partition є різними операціями. Physical split переносить цілі atomic partitions. Один гарячий atomic partition збереже свій serial write boundary після додавання вузлів. Його поділ потребує міграції моделі й explicit зміни batch/unique scope. API не повинен приховувати цю зміну.

Для time series можна оголосити affinity `(tenant, seriesGroup, timeWindow)`. Читання довгого інтервалу торкається кількох partitions. Для global CRUD із випадковими IDs доцільний hash scheme. Універсальний routing policy для всіх workload у першій версії не заявляється.

### 28.4. Placement і replica groups

Control plane може обчислювати suggested placement через weighted rendezvous hashing або інший stable allocation algorithm із disk/CPU/failure-domain constraints. Остаточна routing map фіксується consensus. Жоден hashing algorithm сам по собі не надає durable ownership або fencing.

RF=3 означає три copies кожного physical shard на різних failure domains. При 32 groups на 6 вузлах це 96 replica instances, у середньому 16 на вузол, та приблизно 5–6 leaders за рівномірного розподілу. Це приклад арифметики конфігурації. Повністю незалежні fault domains перевіряються deployment policy.

У KeyLoad data mutations реплікуються в ordered log кожної group. Metadata має окрему group. Qdrant документує Raft для topology/metadata; такий факт не підтверджує ідентичний consensus path кожної data mutation. Власні data guarantees KeyLoad перевіряються незалежно. [S26]

Multi-group implementation і transport multiplexing є додатковими qualification gates для .NEXT. Наявність одного робочого Raft cluster у sample не доводить масштабованість десятків/сотень groups у спільному host. На старті кількість groups свідомо обмежується, heartbeats/WAL handles/memory вимірюються.

### 28.5. Read scaling і fan-out

Point query з partition key прямує до одного shard. Scalar equality на projectId або time window повинна відсікати зайві shards. Query без routing predicate може потребувати всіх shards; planner показує fan-out та перевіряє бюджет.

Follower serving допускається за declared read contract: stale mode або read barrier і applied-position check. Read replica для ANN також має повідомляти projection watermark. Сам факт наявності follower не означає readiness його search index.

Розподіл search work виконується через bounded per-shard fragments. Не всі combinations client×shard×branch запускаються одночасно. Для важких запитів застосовуються tenant queue, node queue, concurrency cap і cancellation propagation. Hedged reads можна додати пізніше з duplicate-work budget і тим самим required cut.

### 28.6. Migration протокол

Durable states: `Planned → Copying → CatchingUp → Validating → FencedCutover → Routed → Retiring → Completed`. У state зберігаються source/destination, routing epoch, snapshot position, policy/catalog version, checksum і progress.

Після cutover source відхиляє нові writes зі старим epoch; router повторює з новою mapping і тим самим CommandId. Derived search indexes можуть бути перенесені разом зі snapshot або rebuilt, із readiness gate до serving. Старі read leases/cursors або завершуються на старій generation, або отримують explicit migration/token outcome.

CommitToken із group-local log position потребує durable translation через migration lineage. Source token не можна механічно порівняти з log index нової group. Альтернативний дизайн має stable per-atomic-partition sequence, replicated разом із canonical changes; вибір фіксується ADR-017.

### 28.7. Regions

Перший replicated deployment працює в одному регіоні з кількома failure domains. Віддалений регіон може отримувати asynchronous disaster-recovery copy з explicit RPO. Synchronous multi-region quorum додає network round trips до commit path. Active-active writes, conflict resolution і global unique constraints залишаються окремим проєктом.

## 29. Користувачі, ролі та авторизація

### 29.1. Minimal security model

Користувачі представлені principals двох типів: human і service account. Human authentication може підключатися до OIDC provider; перший автоматизований серверний сценарій використовує scoped API keys. Ключ має випадковий high-entropy secret, server-side verifier, expiry, scopes і revocation state. Plain API secrets не зберігаються в логах чи доступному списку principals.

Authorization hierarchy: cluster → tenant → database → collection → row → field. RBAC задає дозволені операції, обмежені row policies задають owner/team/project scope. Обчислення політики є чистою bounded операцією над verified principal context і authoritative metadata. Arbitrary SQL/C# в політиках першої версії відсутній.

Admin capabilities розділяються: infrastructure operations, schema/index management, security-policy management і raw-data access. Право перевіряти node health не повинно автоматично давати читання sensitive values. Фізичний доступ адміністратора до process memory, disk та encryption keys залишається за межами field-masking threat model.

### 29.2. Дозволи

| Capability | Приклад області |
|---|---|
| documents.read / documents.write | Database або collection; write policy додатково перевіряє row і поля |
| query.execute | Дозволені collections та максимальний query profile |
| graph.traverse / graph.write | Graph ID, labels, direction, vertex/edge policies |
| series.read / series.append | Entity/series scope та retention limits |
| vector.search / vector.readRaw | Named vector field; raw embedding може мати власну sensitive classification |
| field.readRaw / field.use | Окремі paths/classifications і дозволені usage operations |
| schema.manage / index.manage | Створення generation, DDL, reindex budgets |
| data.export / changes.subscribe | Logical export/CDC із поточною projection policy |
| backup.manage / backup.restore | Privileged raw artifacts та окремий target environment |
| query.diagnose / audit.read | Пояснення plans, authorized metrics, audit metadata |
| events.append/read/replay | Stream sets, streams, generations та classified fields |
| queue.publish/consume/ack/renew | Queues/lanes, delivery tokens та current consumer grants |
| queue.inspect / deadletters.read/redrive | Metadata/body policy і explicit redrive |
| subscriptions.manage / scheduler.manage | Group source/filter scope, checkpoints, schedules |

Політика за замовчуванням deny. Grants обмежують scope. Explicit deny має визначений пріоритет. Tenant membership задається server-side identity mapping. SessionGrain не приймає role list або tenant authority з довільного request payload.

### 29.3. Row policy

Початкові вирази: tenant equality, ownerId equality, membership у declared group/project set, conjunction/disjunction із bounded кількістю умов. ACL може бути inline metadata або посиланням на versioned ACL record. Ownership і ACL fields записуються тільки спеціально авторизованою mutation; generic document patch не обходить цю перевірку.

На query path row policy може компілюватися в bitmap/ID allowlist. Це acceleration cache. Final document materialization звіряє актуальний ACL metadata version і current principal context. Cached grant, що став застарілим, не дає права повернути документ після визначеної revocation barrier.

Офіційний PostgreSQL RLS дає корисний baseline для policy semantics та default-deny testing. Документація окремо описує винятки для owners/superusers/BYPASSRLS; benchmark ролі мають працювати без таких bypass. [S35]

### 29.4. Відкликання дозволів

Сильний запропонований контракт: після підтвердження security change наступний новий request або streaming page використовує новий policy epoch. MVP може отримувати current policy epoch через metadata read barrier із batch/coalescing. За недоступності перевірки confidential data path завершується fail-closed.

Permission caches key-уються tenant, principal, membership version, policy epoch та scope. Інвалідація повідомленням сама по собі не є доказом негайного revocation. Подальша lease-based оптимізація повинна визначити, коли policy-change ACK чекає завершення старих leases і які bound assumptions виконує система.

Row ACL mutations живуть у canonical data scope. Query, що використовує cached allowlist, повторно перевіряє latest row authorization для фінальної видачі. Для строгої graph-path revocation потрібні current checks під час traversal та на етапі формування дозволеного результату. Уже виконаний in-flight request має documented snapshot/page boundary; жодна обіцянка вилучити раніше передані bytes не надається.

### 29.5. Isolation і abuse controls

Усі lookup keys, caches, cursors, jobs і outbox subscriptions містять tenant scope. Cross-tenant key lookup повинен завершуватися generic not-found/denied outcome за сталою documented policy, без підтвердження існування чужого ID.

Tenant quotas включають число collections/indexes, storage size, active series, vector dimensions/count, document size, query CPU/memory, concurrent queries, graph fan-out, bulk batch bytes та background reindex quota. Node governor застосовує загальні межі до всіх tenants; один tenant не може використати суму всіх per-query maximums до OOM.

## 30. Sensitive/PII fields і маскування

### 30.1. Контракт мінімальної реалізації

Адміністратор позначає field path як sensitive. За замовчуванням raw значення такого поля виключається з відповіді. Доступ до raw field, використання поля у predicate та його індексація регулюються окремими правилами. Sensitive classification не змінюється через звичайний update документа.

Це контроль доступу та output redaction на рівні БД. Original values залишаються в authoritative storage. Encryption at rest, TLS, backups, адміністративний доступ і audit потребують власної конфігурації. Автоматичне виявлення всіх PII у довільному тексті у minimal release відсутнє.

Dynamic Data Masking у SQL Server демонструє важливе обмеження: приховані output values можна інферувати через дозволені predicate-запити. Тому KeyLoad перевіряє field usage до виконання запиту. [S40]

### 30.2. Проєктна schema policy

```json
{
  "collection": "customers",
  "policyVersion": 1,
  "fieldPolicies": [
    {
      "path": "/email",
      "classification": "pii.email",
      "defaultOutput": "omit",
      "rawReadPermission": "pii.read",
      "rawUsagePermission": "pii.query",
      "textIndex": false,
      "embeddingSource": false
    },
    {
      "path": "/phone",
      "classification": "pii.phone",
      "defaultOutput": "omit",
      "rawReadPermission": "pii.read",
      "rawUsagePermission": "pii.query",
      "textIndex": false,
      "embeddingSource": false
    },
    {
      "path": "/contacts/*/email",
      "classification": "pii.email",
      "defaultOutput": "omit",
      "rawReadPermission": "pii.read",
      "rawUsagePermission": "pii.query",
      "textIndex": false,
      "embeddingSource": false
    }
  ]
}
```

`*` тут є власним wildcard розширенням field-policy path для елементів array. Це не стандартний JSON Pointer token. Parser зберігає path як структуровані сегменти, перевіряє escaping і застосовує ті самі правила до nested projections, aliases, wildcards і computed expressions. Alternative schema може задавати правила за field IDs; public syntax закривається KL-062.

Для JSON response без `pii.read`:

```json
{
  "id": "customer-17",
  "name": "Example Customer",
  "_meta": {
    "revision": 12,
    "redactedFields": ["/email", "/phone"]
  }
}
```

Для principal із documents.read та відповідним raw-read grant response може включати email/phone. Присутність `redactedFields` сама дає schema information; її показ регулюється schema visibility policy. Для прихованої schema повертається generic redaction indicator без paths.

### 30.3. SQL output

`SELECT *` опускає sensitive fields без raw grant. Явний `SELECT email` може повернути typed null із redaction metadata, якщо policy дозволяє masked selection. Це відрізняється в metadata від actual stored null. Для schema-hidden fields binder повертає generic denied/unknown-field outcome.

Computed expression, alias і JSON extraction не знімають classification: `LOWER(email)`, `SUBSTRING(email,...)`, `HASH(email)`, JSON object wrapping або fallback expression отримують derivative classification і перевірку permissions. Мінімальний варіант відхиляє обчислення над raw-sensitive source за відсутності `field.use`.

### 30.4. Usage controls

| Запит без відповідного raw-use grant | Очікувана поведінка |
|---|---|
| SELECT * | Дозволені поля, sensitive paths опущені |
| SELECT email | Masked output або generic denial згідно з policy |
| WHERE email = @value | Відхилити до index lookup |
| ORDER BY phone | Відхилити до sort/index access |
| GROUP BY email / COUNT(email) | Відхилити; field usage може розкрити значення/наявність |
| WHERE email IS NULL / IS MISSING | Відхилити, якщо usage на цьому path закритий |
| Text match/highlight по sensitive field | Відхилити; індексація default disabled |
| VECTOR search у derived-sensitive space | Відхилити без permission цього vector space |
| Export / CDC / live query | Той самий secure projector і новий auth checkpoint |
| Explain/Profile/error | Без raw values, guarded statistics і paths |

Partial masking, наприклад останні 4 символи, є дозволеним disclosure policy. Перший реліз може обмежитися omit, щоб зменшити кількість side channels. Вбудований masking не заявляє differential privacy чи повної невідрізнюваності timing/access patterns.

### 30.5. Write semantics

Principal може мати permission записувати певне sensitive поле без права його читати, наприклад ingestion service. Write API перевіряє allowed paths, expected revision і зміну classifications. Masked response не повинен випадково використовуватися як destructive full replacement.

Тому SDK зберігає metadata redaction. Повний PUT із redacted source object вимагає explicit replacement intent та permissions для всіх sensitive fields, які він може стерти. Безпечний звичайний шлях: PATCH дозволених paths із CAS. `Remove` sensitive field перевіряється як sensitive mutation; empty/missing/null мають різні semantics.

Client-provided sensitivity hint може підвищити classification тільки за визначеною policy. Зниження classification, ввімкнення `embeddingSource` або перенесення sensitive data у unrestricted field потребує privileged workflow. Вільний текст може містити непозначені PII; відповідальність за класифікацію/ingestion validation описується окремо.

### 30.6. Індекси, embeddings і backups

Schema classification зміна інвалідує query plans та affected projection generations. Sensitive text, який уже потрапив у postings/stored snippets, вилучається або отримує окремий authorized index до serving. Derived embeddings мають source-field lineage; unsafe space зупиняє serving до rebuild або permission correction.

Raw data може лишатися в WAL, older revisions, snapshots і backups до retention/purge. Адміністративне логічне видалення не є обіцянкою негайного фізичного стирання всіх копій. Purge jobs мають scope, retention status і audit. Field masking не застосовується як трансформація canonical replication; replica transport і raw backup destination мають privileged authenticated boundary.

### 30.7. Мінімальний privacy test suite

Tests покривають aliases, nested arrays, quoted paths, projection wildcards, functions, filters, sort/group/facets, generated snippets, vector fields, graph paths, cursor reuse, revoked key, changed role і exports. Канаркове sensitive значення не повинно з'являтися у response, trace, exception text, audit payload чи authorized-to-outsider inference request.

Окремий adversarial test на inference виконує adaptive predicates по закритому полю; binder повинен відхилити їх до access. Row-count запити бачать тільки дозволений row scope. Strong noninterference між overlapping ACLs у shared statistics/ANN потребує окремого security-domain design та не закривається цим minimal suite.

## 31. Звідки може взятися швидкість KeyLoad

### 31.1. Performance hypothesis

Основна гіпотеза: entity/affinity-aware layout, короткий prepared query path, batched adjacency та data-local hybrid execution можуть зменшити latency і resource cost для project-scoped mixed workloads. До benchmark це інженерна гіпотеза.

Для general relational OLTP і довільних joins перевага не встановлена. Першими workloads для перевірки обираємо project-scoped search, graph-neighborhood retrieval, document CAS із кількома індексами та batch ingestion подій.

### 31.2. Оптимізації у порядку перевірки

Спочатку прибираємо непотрібний fan-out і зайві reads. Далі зменшуємо materialization/copy cost. Потім налаштовуємо local parallelism, SIMD і batching. На кожному етапі benchmark зберігає correctness, durability та permission profile.

Late materialization передає між операторами IDs, revisions і scores. JSON читається для фінальних hits або дозволеного residual predicate. Index covering fields застосовуються тільки якщо їхня classification дозволяє таке використання.

`RecordBatch` executor може зберігати потрібні typed columns для кількох сотень rows. Це execution layout; canonical payload лишається JSON. Batch sizes 128/256/512 є кандидатами benchmark. Large buffers мають lifetime/lease contracts; native/managed RSS і allocation pressure вимірюються разом.

Plan cache key містить normalized AST, schema/index versions, field-policy shape і ranking profile. Principal-specific allowlists зберігаються окремо з policy epoch. Plan cache не містить literal PII і не переносить grants між principals.

### 31.3. Межі швидкості

Повний float32 scan 1M vectors × 1536 dimensions читає щонайменше 6.144 GB raw vector bytes. За **умовної ефективної пропускної здатності 40 GB/s** ідеальний час одного тільки читання становить `6.144/40 = 0.1536 s`. Це обчислення за припущенням, яке ще потрібно виміряти на конкретній машині; distance computation, heaps, concurrency та storage додають витрати.

Якщо scalar/ACL/graph filters залишили 1000 vectors, raw bytes для scoring становлять приблизно 6.144 MB. Ця різниця обґрунтовує adaptive exact-vs-ANN planning. Ціна побудови allowlist і random memory access входить у end-to-end query latency.

Fan-out впливає на хвіст latency. За спрощеного припущення незалежності та 99% ймовірності, що кожен із 32 shards відповість у межах заданого часу, ймовірність своєчасної відповіді всіх становить `0.99^32 ≈ 72.50%`. Реальні затримки можуть корелювати; приклад демонструє потребу в pruning, budgets і slow-shard metrics.

Для 32 shards × 200 candidates × 3 branches × умовних 80 bytes на candidate мережевий payload становить приблизно 1.536 MB до protocol overhead. Передавання повних JSON і vectors на coordinator суттєво збільшило б цей обсяг. Actual candidate record size перевіряється serialization benchmark.

### 31.4. Memory та disk budget

Budget вузла = canonical memtables/cache + graph/index structures + query working sets + compaction/rebuild reserves + runtime + replica logs. Окремі caches не можуть кожен трактувати всю RAM як власну межу.

RF множить canonical/replica storage; durable logs, projection snapshots і compaction мають власне write amplification. Rebuild двох index generations одночасно враховується в admission. Низький free disk переводить node в controlled throttling/read-only/fail-stop outcome до хибного durable ACK.

Публічний QPS без document size, index count, durability, hardware, concurrency, recall та filter distribution не використовується як performance evidence. Збереження цих dimensions є частиною benchmark artifact.

## 32. Відтворюване порівняння з PostgreSQL

### 32.1. Baselines

**PG-A:** PostgreSQL 18 із pinned patch, `jsonb` documents, налаштованими B-tree/expression/GIN indexes, edge adjacency tables та time partitioning за потреби. Full-text uses documented tsvector/tsquery ranking. Конкретні DDL/plans входять до benchmark repository. [S36][S37]

**PG-B:** той самий PostgreSQL із pinned pgvector, exact vector search, HNSW/IVFFlat, iterative scans, explicit `ef_search`/probes, filter indexes та partition strategies. Порівнюється recall під тим самим allowed-ID predicate. [S38]

**PG-C:** PostgreSQL із BM25 extension, наприклад ParadeDB pg_search, плюс pgvector для hybrid search. Офіційний матеріал ParadeDB описує BM25 + pgvector + RRF. Кожна версія extension окремо перевіряється щодо PostgreSQL support, license, replication і deployment options. [S41]

PG-A full-text ranking і BM25 дають різні relevance functions. Чисте latency comparison без relevance evaluation було б неповним. PG-C додає ближчий lexical ranking baseline; власні profile/weights/candidate windows контролюються benchmark.

### 32.2. Fairness rules

Однакові datasets, embeddings, query distribution, результат projection, TLS/network path, acceptable freshness і durability contract. PostgreSQL benchmarks використовують prepared commands і connection pool. Indexes створюються та statistics оновлюються до warm benchmark; ingestion/update-under-load tests проводяться окремо.

Порівнюються два рівні: direct database API та однаковий minimal application endpoint поверх кожної БД. Результати embedded KeyLoad окремо позначаються in-process і не змішуються з remote PostgreSQL results.

Для single-node durability PG має `fsync=on`, `synchronous_commit=on` і звичайні persistence guarantees. Для RF3 comparison PG synchronous replication конфігурується з підтвердженням від потрібної кількості standbys; topology, synchronous_standby_names та failure assumptions фіксуються. `synchronous_commit=on` у documented synchronous setup чекає durable standby flush. Exact guarantee equivalence перевіряється fault tests. [S39]

Для authorization baseline PG використовує nonowner/non-BYPASSRLS role і відповідні policies. Порівняння з KeyLoad включає masking/projection overhead, коли ця функція ввімкнена. Окремий no-policy benchmark дозволений як microbenchmark із правильним label. [S35]

PG JSONB GIN indexes створюються для відповідних операторів; expression indexes для hot typed fields, composite indexes для filter+sort. Graph baseline використовує adjacency B-tree indexes, batched SQL/recursive CTE з такими самими depth/visited budgets. Vector benchmark під selective filter використовує iterative scan чи exact fallback, коли це вигідно. [S37][S38]

### 32.3. Workload matrix

| ID | Сценарій | Основний контроль |
|---|---|---|
| B01 | Prepared Get/MultiGet, payload 1/4/16 KiB | p99, throughput, bytes/op |
| B02 | Put/Patch/CAS, 0/3/10 strict indexes | Durable ACK, conflicts, write amplification |
| B03 | Scalar filter + composite sort + cursor | Scanned records, stable order, allocations |
| B04 | Graph 1/2/3/5 hops, cycles, supernodes | Результат reference traversal, RPC rounds |
| B05 | Exact/ANN search, filters 100/10/1/0.1% | Recall@10/@100, latency, memory |
| B06 | Filter correlation: independent/positive/negative | Recall collapse, exact fallback cost |
| B07 | BM25 + vector + graph scope + same safe projection | nDCG@10, candidate recall, p99 |
| B08 | Seeded graph retrieval + three-way fusion | Exact profile fixtures, end-to-end relevance |
| B09 | Series batch append/range/rollups/late events | Bytes/sample, aggregates, ingestion lag |
| B10 | Concurrent updates + reindex + compaction | Tail latency, freshness, bounded RSS |
| B11 | Tenant skew: small shared/large dedicated/hot key | Fairness, overload outcomes, routing cost |
| B12 | ACL revoke/masking/CDC/cursor continuation | Zero forbidden output за declared boundary |
| B13 | Node loss, network partition, retry storm | Durable outcomes, recovery, error rate |
| B14 | Physical shard split/move під навантаженням | Routing/token correctness, p99, catch-up |

Dataset sizes: small 100k, main 1M, large 10M або достатній обсяг для working set понад RAM. Corpus включає український/англійський текст, identifiers, short/long documents і heterogeneous schemas. Embeddings однакові між системами; model inference time винесений в окрему метрику.

### 32.4. Навантажувальна методика

Мікротести вимірюють allocation і operator CPU. End-to-end tests використовують як closed-loop clients, так і open-loop offered load, щоб побачити queue buildup і overload. У raw results зберігаються scheduled start, actual start, completion, deadline/error та bytes. Timeouts і rejects не вилучаються з report.

Warm-up, JIT, cache warming, checkpoint/compaction states і restart recovery документуються. Для steady-state comparison проводяться повторні runs із тим самим seed і кілька різних seeds. Confidence intervals або variability report доповнюють medians; один найкращий run не є підсумком.

Стосовно scale-out порівнюються 1/3/6 nodes із зафіксованими total resources і replication factors. PostgreSQL partitioning на одному сервері не називається distributed sharding. Для distributed PostgreSQL arm необхідно явно вибрати й кваліфікувати окремий sharding stack; до цього single-node PG залишається single-node baseline.

### 32.5. Критерій «є сенс продовжувати»

Перед тестом затверджується один primary workload і один primary target. Приклад цілі: **щонайменше 2× useful throughput при однаковому p99 SLO**, hardware budget, quality floor, projection freshness і durability. Alternative milestone може використовувати 50% latency reduction за однакового offered load; перемикати criterion після перегляду результатів без позначки exploratory заборонено.

Усі outcomes цієї редакції мають статус «не виміряно». Досягнення цілі у одному workload не поширюється на всі SQL, graph і vector запити. За провалу gate виконується operator-level аналіз: read-view serialization, fan-out, allocations, index layout, disk або ANN recall. Рішення про зміну архітектури спирається на ці дані.

Для продукта додатково оцінюються deployment complexity, кількість компонентів, migration cost і .NET developer experience. Вони доповнюють performance evidence; вимога benchmark проти PostgreSQL зберігається.

## 33. Корисні функції після основного query path

До раннього product scope доречно включити Explain/Profile, command outcome lookup, resumable CDC, provenance/source references, index status і readable diagnostics. Вони потрібні для розуміння помилок та реальної експлуатації.

Resumable CDC/live queries використовують dedicated change-feed profile; durable business subscriptions і queue delivery описані у 37–44. Live queries будуються як query subscription на committed change stream із власними квотами. SQL WHERE, permissions і masking однакові зі звичайним query. Перші subscriptions можуть обмежуватися простими scalar predicates. Підписка на довільний expensive graph/ANN query із reexecution на кожну зміну потребує окремого cost contract.

Document revision history має configurable retention, storage quota та audit метадані. TTL-події, tombstones і outbox дають основу для видалення та reprocessing; історія sensitive values залишається під тими самими raw-read grants.

Named vector spaces, multivector/chunk grouping, sparse retrieval, optional reranker і reusable ranking profiles додаються після стабілізації candidate semantics. Query feedback зберігається з privacy policy; learning-to-rank потребує окремого train/validation/held-out split.

Index advisor спочатку рекомендує index на основі redacted query shapes і explain statistics. Automatic index creation має hard quotas й approval policy. Довільне завантаження користувацьких assemblies, SQL UDF, inference models або відкриття filesystem paths у server process лишається поза раннім scope.

## 34. Як зібрати реалізацію і довести вертикальний сценарій

### 34.1. Межі solution

До структури розділу 18 додаються `KeyLoad.Query` із Syntax/Binding/Planning/Execution і `KeyLoad.Security` із Principals/Policies/Classification/Projection. На початку це можуть бути namespaces у кількох assemblies. Public SDK залежить тільки від versioned contracts; storage, parser і search internals у SDK відсутні.

```text
KeyLoad.Client ──────────────────────── KeyLoad.Contracts
                                             ▲
KeyLoad.Server ── Query ── Security ── Core ───┤
                    │                  │      │
                Search adapters     Storage.ZoneTree
                    │                  │
                Orleans routing ── PartitionHost
                                       │
                          Replication + Catalog
```

Arrows тут показують логічні зв'язки; compile-time dependency graph фіксується architecture tests. Security policy types і entity identity знаходяться у contracts/core, щоб Query і Security не утворювали циклічних project references. Server composition root підключає implementations через DI.

### 34.2. Startup

Standalone: read config і NodeId → exclusive data lock → validate format → recover canonical storage → load catalog/security → start local Orleans host → recover index generations → ready endpoints. Readiness повідомляється окремо для canonical CRUD і конкретних search capabilities.

Cluster: config/seeds → authenticated metadata transport → catalog quorum readiness → Orleans membership adapter → replica recovery/readiness → public routing. Відкриття іншого каталогу з тим самим NodeId/ReplicaId виявляється до serving. Public listening і accepted data traffic мають різні readiness checks.

`IHostedService`-компоненти мають визначений start/stop order. Shutdown зупиняє admission, drains bounded jobs, завершує writers/readers, persist checkpoints, закриває maintainer та files. Таймаут shutdown повертає явний стан; recovery наступного start не покладається на те, що shutdown був чистим.

### 34.3. Перший вертикальний slice

Одразу створюються tenant, service account, TransactionDomain `customer-processing`, collection `customers`, stream set `customer-events`, queue lane, composite index і sensitive email policy. SDK одним atomic command записує документ, explicit domain event і local queue message. Consumer slice перевіряє claim/ACK, retry та inbox/processing effects. Один principal читає masked результат, інший має raw grant. Scalar SQL та JSON-query повертають однаковий allowed result.

Тест виконує CAS update та повторення CommandId, вбиває process, запускає новий, перевіряє document/index/dedup invariants, робить backup і restore в порожній каталог. Цей самий сценарій має PostgreSQL baseline для B01/B02/B03/B12. Функція masking вводиться до першого real-data використання.

### 34.4. Другий slice

До стабільного ядра додаються graph contains/related_to, exact vectors та BM25 projection. Три branches спочатку працюють на малому dataset з exhaustive oracle. SQL SEARCH, JSON envelope і C# builder породжують equivalent logical plan. Tests перевіряють GraphScope, ranks, redaction і projection lag.

Мінімальні event/queue commit і recovery gates із розділу 45.4 проходять до розширеного ANN milestone. Після exact retrieval підключається managed ANN. На тому самому corpus порівнюються exact/ANN recall, selective filters та combined relevance. Concurrency вводиться з bounded pools і правильними read views. Early cluster spike із KL-017..KL-021 виконується паралельно після стабілізації command contract.

### 34.5. Третій slice

Три вузли, кілька groups, RF3, shard-local retrieval, global branch merge і policy epochs. Далі додаються node loss, retry, stale routing, shard movement і security revocation під active queries. Усередині цього slice рахується реальна ціна distribution та проходить QuorumDurable qualification.

Перший production-candidate release обмежує query language і deployment topology до перевіреної матриці. Кожен unsupported path має explicit error. Закриття happy-path demo не дорівнює закриттю durability, permissions чи migration acceptance.

## 35. Додатковий backlog: KL-045..KL-080

Ці задачі доповнюють KL-001..KL-044. P0/P1 foundation tasks виконуються до роботи з реальними даними. Search/cluster-specific tests активуються, коли відповідна capability заявляється в release manifest. Залежності описують DAG робіт; номер задачі не є календарним порядком.

### KL-045 · Query capability manifest і AST version

**Етап:** P0. **Залежності:** KL-002.

**Робота:** Визначити supported statements, expressions, named parameters, types, protocol version і stable errors для SQL/JSON/SDK.

**Приймання:** Кожен public operator має semantics, cost class і permission requirements; unknown/unsupported operator відхиляється до execution; AST roundtrip fixtures збережено.

### KL-046 · SQL dialect semantics

**Етап:** P0. **Залежності:** KL-045.

**Робота:** Зафіксувати null/missing, numeric promotion, JSON paths, collation, deterministic order, endpoint filters і SEARCH eligibility.

**Приймання:** Є executable semantic fixtures для boundary cases; SQL-підмножина має versioned specification; full PostgreSQL compatibility не заявляється.

### KL-047 · SQL parser adapter і diagnostics

**Етап:** P1. **Залежності:** KL-001, KL-045, KL-046.

**Робота:** Кваліфікувати SqlParser-cs або власний bounded parser, підключити dialect extensions, source spans і error positions.

**Приймання:** Fuzzed malformed/deep/oversized input не створює unbounded CPU/RAM; parsed unsupported SQL дає capability error; lexical errors не відображають sensitive parameters.

### KL-048 · Binder і field lineage

**Етап:** P1. **Залежності:** KL-047, KL-015.

**Робота:** Names → catalog IDs, type inference, function whitelist, dependency lineage для expressions і field-use permissions.

**Приймання:** Alias/nested extraction/function не обходять field restriction; unknown types і unsafe casts відхиляються перед storage access; principal-scoped binds повторювані.

### KL-049 · Rule-based optimizer

**Етап:** P1. **Залежності:** KL-048, KL-013.

**Робота:** Index matching, route pruning, bounded sort/aggregate, residual predicates, protected policy barriers і plan-cache signature.

**Приймання:** Optimized і reference plans дають однаковий результат; unsafe policy reordering заборонене; estimated scan/fan-out видимі в Explain.

### KL-050 · Batch executor і secure operator boundaries

**Етап:** P1. **Залежності:** KL-049, KL-004.

**Робота:** Leased read views, RecordBatch pipeline, bounded queues, late materialization і cancellation.

**Приймання:** Operators звільняють leases/buffers при cancel/exception; scan не читає partial transaction; batch allocations і queue bounds виміряні.

### KL-051 · SQL/JSON/C# equivalence

**Етап:** P1. **Залежності:** KL-050, KL-014.

**Робота:** Public query endpoints, typed parameters/vector attachments і builder lowering в той самий AST.

**Приймання:** Три входи мають equivalent normalized plans/results/errors; unsupported LINQ не переходить до implicit client evaluation.

### KL-052 · Ранній admission control

**Етап:** P1. **Залежності:** KL-015, KL-006, KL-009.

**Робота:** Per-principal/tenant/node concurrent query limits, payload/depth/CPU/memory/deadline caps і write backpressure.

**Приймання:** Overload дає bounded queue/RSS і stable errors; cancel реально припиняє роботу; control traffic не блокується heavy-query burst. Розширений governor залишається у KL-040/KL-076.

### KL-053 · Session facade та independent query contexts

**Етап:** P1. **Залежності:** KL-014, KL-052.

**Робота:** Optional SessionGrain для token/subscription state; звичайні queries виконує independent context із data-local routing.

**Приймання:** Один blocked query не блокує всі requests principal; idle sessions не тримають index handles; grain interleaving не змінює security context.

### KL-054 · SQL differential і metamorphic suite

**Етап:** P1. **Залежності:** KL-051.

**Робота:** Reference evaluator, equivalent predicate transformations, prepared/parsed path, scalar reference PG cases у спільній семантиці.

**Приймання:** Generated fixtures покривають null/missing/Unicode/bounds; index/scan plans збігаються; intentional dialect differences явно збережені.

### KL-055 · GraphScope / GraphRetriever / GraphExpansion

**Етап:** P3. **Залежності:** KL-023, KL-045.

**Робота:** Окремі AST operators, seed refs, shortest-hop score, endpoint-only predicates і authorized path semantics.

**Приймання:** Hidden vertex policy дотримано; endpoint predicate не видаляє валідний intermediate type; cycles/path duplicates не множать rank contribution.

### KL-056 · Three-way hybrid retrieval

**Етап:** P4. **Залежності:** KL-033, KL-055, KL-050.

**Робота:** Named text/vector/graph branches, same EntityRef identity, weightedRrfV1, version validation, refill і candidate windows.

**Приймання:** Exact small-corpus oracle збігається; missing branch contribution нульовий; duplicate/revoked/stale hit не займає фінальний rank.

### KL-057 · Global branch ranking

**Етап:** P5. **Залежності:** KL-037, KL-056.

**Робота:** Порівнянні modality scores, global windows перед fusion, stable tie-break, bounded transport і completeness metadata.

**Приймання:** Exact results зберігаються після зміни shard layout за незмінних corpus/cut; ANN/window error позначений; втрата shard не маскується як повна відповідь.

### KL-058 · BM25 corpus і statistics epochs

**Етап:** P4. **Залежності:** KL-029, KL-015.

**Робота:** Tenant/security-corpus IDF, persisted versioned statistics, guarded diagnostics і local-approximate profile.

**Приймання:** Дані іншого tenant не входять у corpus; всі shards strict profile використовують той самий statistics epoch; overlapping-ACL limitations задокументовано.

### KL-059 · Managed ANN implementation qualification

**Етап:** P4. **Залежності:** KL-030, KL-027.

**Робота:** Оцінити managed HNSW codebase/implementation, packing, persistence, delta/tombstones, platform compatibility та transitive dependencies.

**Приймання:** Required targets працюють без mandatory native ANN package; search/update/delete/rebuild tests пройдено; recall/RSS/allocations порівняні з exact і optional external baseline.

### KL-060 · Adaptive filtered retrieval

**Етап:** P4. **Залежності:** KL-032, KL-059.

**Робота:** Allowed cardinality, small-set exact, filtered ANN expansion, budgeted fallback і correlation cohorts.

**Приймання:** Recall target перевірено окремо для 100/10/1/0.1% filters і correlation; traversal topology не обрізається наївним output filter; actual fallback costs видимі.

### KL-061 · Principals, API keys і RBAC

**Етап:** P1. **Залежності:** KL-015.

**Робота:** Human/service accounts, trusted tenant mapping, scoped key verifier, expiry/revocation, role grants і administrative separation.

**Приймання:** Forged principal/tenant/roles у JSON і grain calls відхиляються; revoked/expired keys не приймаються за declared barrier; plaintext secrets не логуються.

### KL-062 · Sensitive field catalog

**Етап:** P1. **Залежності:** KL-061, KL-045.

**Робота:** Versioned path classifications, nested/array matching, read/use/output permissions і privileged schema mutations.

**Приймання:** Plain document patch не знижує classification; quoted/escaped paths мають один canonical meaning; catalog change інвалідує affected plans.

### KL-063 · Sensitive expression usage enforcement

**Етап:** P1. **Залежності:** KL-062, KL-048.

**Робота:** Перевіряти SELECT/functions/WHERE/ORDER/GROUP/COUNT/EXISTS/facets/retrieval за повною field lineage.

**Приймання:** Adaptive predicate attack відхиляється до storage lookup; aliases/functions/JSON wrappers не обходять restriction; error payload sanitized.

### KL-064 · Єдиний safe projector

**Етап:** P1. **Залежності:** KL-063, KL-050.

**Робота:** Omit і typed redacted output, generic/path metadata policy, protected PUT/PATCH roundtrip; підключення Get/Query/Export/CDC.

**Приймання:** Sensitive canary відсутній у всіх unprivileged outputs/logs; authorized raw read працює; masked object не стирає hidden values без дозволу.

### KL-065 · Row ACL planner і graph enforcement

**Етап:** P3; scalar частина P1. **Залежності:** KL-061, KL-023.

**Робота:** Owner/team/project predicates, generation-scoped ID sets, final row validation, vertex/edge traversal policy.

**Приймання:** Cross-tenant IDs не збігаються в cache; stale grants не проходять final authorization; graph hidden paths не з'являються у response/Explain.

### KL-066 · Revocation epochs і cursor reauthorization

**Етап:** P2/P5; local policy частина P1. **Залежності:** KL-061, KL-021, KL-064.

**Робота:** Policy read barriers, cache epoch, streaming page checkpoints, revocation ACK contract і fail-closed behavior.

**Приймання:** Після policy-change ACK нові requests/pages використовують новий epoch; недоступність authority не повертає sensitive payload; старі cursors reauthorize/invalidate.

### KL-067 · Classification lineage у search projections

**Етап:** P4. **Залежності:** KL-062, KL-029, KL-031.

**Робота:** Source paths для text/vector/snippets, default search exclusion, restricted spaces, reclassification rebuild і safe reranker inputs.

**Приймання:** Hidden raw values не надсилаються reranker-у без grant; старі unsafe generations припиняють serving; source-to-derived purge status видимий.

### KL-068 · Security adversarial matrix

**Етап:** P1..P6 за capabilities. **Залежності:** KL-064, KL-065, KL-066, KL-067.

**Робота:** Cross-interface attacks, row/field inference, alias/path tricks, invalid tokens, revoked sessions, unsafe highlights/export/trace.

**Приймання:** Forbidden output zero для supported threat model; відомі timing/statistics limitations явно зазначені; regressions блокують відповідний release profile.

### KL-069 · Atomic/physical partition contracts

**Етап:** P2. **Залежності:** KL-002, KL-019.

**Робота:** Stable AtomicPartitionId, virtual buckets, physical shards, routing schemes і explicit atomicity/unique scope при packing/split.

**Приймання:** Node-count change не міняє identity; unsupported atomic split відхиляється; batch scope зберігається після migration.

### KL-070 · Placement і tenant packing

**Етап:** P5. **Залежності:** KL-069, KL-036.

**Робота:** Capacity/failure-domain aware placement, shared small tenants, dedicated large tenants, persisted placement intents.

**Приймання:** RF copies розміщуються за anti-affinity policy; overload tenant throttled; перестановка node membership не дає двох writers.

### KL-071 · Physical shard split/merge

**Етап:** P5. **Залежності:** KL-070, KL-035.

**Робота:** Перенесення цілих atomic partitions, copy/catch-up/cutover, generation readiness і restartable cleanup.

**Приймання:** Crash на кожному state без втрати committed data; unique/batch scope збережено; один hot atomic partition не ділиться мовчки.

### KL-072 · Token migration lineage

**Етап:** P5. **Залежності:** KL-071, KL-021, KL-034.

**Робота:** Translation старих group positions або stable atomic sequences, WaitForIndex після movement, cursor epoch outcomes.

**Приймання:** Старий валідний commit token коректно wait/read після migration або отримує explicit supported invalidation; indices різних logs ніколи не порівнюються без mapping.

### KL-073 · PostgreSQL baseline kit

**Етап:** P0/P1. **Залежності:** KL-006, KL-001.

**Робота:** Reproducible PostgreSQL18/pgvector configs, DDL, prepared clients, RLS role, JSON/index/edge/time fixtures, optional BM25 extension arm.

**Приймання:** Version/hardware/durability/plans збережені; iterative ANN і filter indexes налаштовані; no-policy/no-fsync results мають окремі labels.

### KL-074 · Hybrid quality/performance benchmark

**Етап:** P4. **Залежності:** KL-073, KL-056, KL-060, KL-058.

**Робота:** Identical embeddings/queries, exact eligible-set oracle, recall/nDCG, candidate windows, text scorer variants і masking overhead.

**Приймання:** Primary target обраний до runs; quality/freshness comparable; timeout/error rates входять у report; actual gain або відсутність gain опубліковані.

### KL-075 · Scaling benchmark і fair deployment arms

**Етап:** P5. **Залежності:** KL-073, KL-057, KL-071, KL-072.

**Робота:** 1/3/6 nodes, RF і total resource budgets, shard skew, fan-out, movement, replica recovery та open-loop load.

**Приймання:** Single-node PG не позначений distributed competitor; optional sharded PG arm має окрему конфігурацію; scale gains і tail regressions виміряні.

### KL-076 · Multi-tenant resource governor

**Етап:** P5/P6. **Залежності:** KL-040, KL-052, KL-070.

**Робота:** CPU/memory/native/transitive budget accounting, fair queues, compaction/reindex reservations, hot partition detection.

**Приймання:** Sustained overload без unbounded queues/RSS; noisy tenant не знищує control-plane liveness у declared resource envelope; stop-serving до disk-full ACK error.

### KL-077 · Hot-path allocation і SIMD оптимізація

**Етап:** P4/P5. **Залежності:** KL-006, KL-008, KL-050.

**Робота:** Late materialization, compact candidate transport, pooled RecordBatch, exact vector SIMD, precompiled field paths.

**Приймання:** До/після benchmarks із correctness oracle; buffer reuse race tests пройдено; no hidden memory ownership changes; speedup виміряно окремо від provider changes.

### KL-078 · Time-series chunk qualification

**Етап:** P3/P5. **Залежності:** KL-026, KL-006.

**Робота:** Lossless typed chunk format, timestamp/value codecs, corrections, generation manifest і retention.

**Приймання:** Roundtrip і aggregate oracle для late/equal/edge cases; bytes/sample та rewrite cost виміряні; recovery не втрачає correction chunks.

### KL-079 · Resumable change feed і обмежені live queries

**Етап:** P5. **Залежності:** KL-016, KL-066, KL-064, KL-013.

**Робота:** Protected change projection, cursors, policy recheck, simple predicate subscriptions, delivery/backpressure semantics.

**Приймання:** Reconnect не пропускає committed changes у retention window; duplicates documented/idempotent; old subscription не зберігає revoked grant; feed не повертає raw PII.

### KL-080 · Integrated feature і performance release gate

**Етап:** P6. **Залежності:** KL-044, KL-054, KL-068, KL-074, KL-075, KL-076, KL-077.

**Робота:** Зіставити capability manifest, documentation, benchmarks, privacy tests, read/durability evidence та supported deployment profiles.

**Приймання:** Жоден advertised feature не спирається на mock-only tests; benchmark advantage має точний workload і raw evidence; guarantees/freshness/approximation/limitations відповідають реальній реалізації.

## 36. ADR і наступні інженерні рішення

| ADR | Запропоноване рішення | Що має закрити реалізація |
|---|---|---|
| ADR-012 | KeyLoad SQL subset + versioned extensions | Grammar і semantic fixtures |
| ADR-013 | SQL/JSON/C# → один authorized AST | Equivalence і unsupported-expression tests |
| ADR-014 | RBAC + bounded row/field-use policies | Cross-interface enforcement |
| ADR-015 | Sensitive omit default + derived lineage | Masks, mutation safety, index/reranker privacy |
| ADR-016 | Atomic partitions packed into physical shards | Split/unique/batch invariants |
| ADR-017 | Migration-aware commit/session tokens | Group-position translation чи stable atomic sequence |
| ADR-018 | Global per-modality windows → weightedRrfV1 | Statistics scope, ties, candidate completeness |
| ADR-019 | Managed-first ANN, exact oracle обов'язковий | Qualified library/code, native dependency audit |
| ADR-020 | Optional session facade, незалежні query contexts | No per-client head-of-line blocking, bounded CPU |
| ADR-021 | PostgreSQL як primary performance baseline | Comparable quality/durability/security evidence |
| ADR-022 | Current policy epoch на request/page boundary | Revocation ACK, cache/barrier fail-closed proof |

### 36.1. Питання, які залишаються qualification gates

ZoneTree transactional range view і durable flush contract; .NEXT follower persistence та multi-group hosting; managed HNSW persistence/concurrency; text provider BM25 і tokenizer quality; security-domain statistics cost; migration token representation; оптимальна кількість physical shards; configured failure model для distributed graph scope.

Відповіді на ці питання потребують targeted prototypes та actual measurements. У цій редакції описано способи перевірки й конкретні критерії; тверджень про вже досягнутий throughput, crash safety чи benchmark victory немає.

### 36.2. Рекомендований порядок старту

Спочатку KL-001..KL-008, KL-045..KL-046, KL-073 і eventing contracts KL-081. Далі canonical CRUD/index/command path, minimal auth/masking, scalar SQL та append/queue primitives KL-082..KL-088/KL-091/KL-096. Inbox/processing і scheduler/groups додаються за dependency DAG. Ранній replication spike виконується після стабілізації command contract. Наступними йдуть exact тримодальний retrieval, ANN, distributed branch merge, physical movement та measured optimization; загальний порядок наведений у 45.4.

Ключовий demonstrator версії дизайну 0.2: **один проектно-обмежений SQL-запит знаходить дозволені documents/chunks через text, vectors і graph; sensitive fields приховані; Explain показує physical plan; повторний запуск після crash відновлює canonical state; той самий workload має PostgreSQL baseline.**

## 37. All-in-one контракт: дані, події та доставка

### 37.1. Оновлена модель продукту

KeyLoad об'єднує document/KV storage, scalar indexes, property graph, time series, vector/text/hybrid search, Event Store та durable messaging. Спільні компоненти: каталог, transaction domains, authorization, ordered commit, replication, recovery, SDK та resource governor. Можливості вмикаються через capability manifest; конфігурація queue-only може працювати без text/ANN indexes.

Головний новий наскрізний контракт: **змінити документ, додати бізнес-подію та поставити повідомлення у co-located queue lane одним atomic commit**. Усі операції мають резолвитися в той самий AtomicPartitionId через catalog binding із розділу 41. Для destination в іншому atomic partition source commit зберігає transfer intent, а delivery має окремий статус.

ZoneTree документує побудову event streams через ordered key ranges і queue-like storage через keys, claim state та transactions. Протоколи append, consumer ownership і delivery реалізує KeyLoad. Ці building patterns є підтвердженням придатності примітивів; фактичні durability/read-view gates із P0 залишаються обов'язковими. [S46][S47]

### 37.2. Призначення журналів і моделей

| Рівень | Authoritative дані та строк життя | Доступ користувача |
|---|---|---|
| ZoneTree WAL / transaction log | Відновлення локальних committed mutations; trimming за storage protocol | Internal diagnostic boundary |
| Replicated command log | Порядок команд replica group, elections/recovery/snapshots | Internal replication boundary |
| Domain Event Store | Версійовані бізнес-факти; retention за правилами stream set | Append, ReadStream, Subscribe, Replay |
| CDC / projection outbox | Зміни для projections і transfer intents; consumer/rebuild retention pins | Окремий захищений change-feed API |
| Work queue | Payload плюс scheduled/ready/leased/terminal state | Enqueue, Claim, Ack, Nack, Renew |
| Topic / persistent subscription | Retained log плюс незалежний delivery/checkpoint state кожної group | Publish, Subscribe, Seek/Replay за правами |

Event Store отримує власні public identities, retention та schema versions. Періодичне очищення Raft log або projection outbox зберігає бізнес-історію відповідно до її власного контракту. Копіювання internal WAL до публічного event API заборонене: записи мають іншу семантику, payload scope та lifecycle.

### 37.3. Джерело істини для конкретної сутності

Для звичайної collection canonical state знаходиться у документі; CDC описує зміни. Доменну подію додає застосунок явно, коли вона має бізнес-смисл. Записи DocumentUpdated самі по собі не дають повної моделі OrderAccepted чи PaymentAuthorized.

Для event-sourced collection authoritative state знаходиться у stream. Current-state document є projection із SourceStreamRevision і ReducerVersion. Generic PUT/PATCH такого read model закритий для звичайних ролей. Rebuild відтворює deterministic reducer над доступною історією, а external side effects мають окремий delivery pipeline. DocumentRevision для CAS і SourceStreamRevision зберігаються окремо.

Режим authority задається в catalog. Зміна CRUD → event-sourced вимагає migration baseline, stream initialization і перевірки projection; таку зміну не можна виконати простим configuration toggle над існуючими даними.

## 38. Event Store: streams, revisions, replay і snapshots

### 38.1. Event envelope

Пропонований StreamRef включає tenant/database, TransactionDomainId, AtomicPartitionId, StreamSetId, StreamId та StreamGeneration. Generation захищає від використання старих references після explicit delete/recreate. Для звичайного append stream version починається з 1.

```text
EventId                 client-generated stable ID
StreamRef               повна scoped identity
StreamRevision          монотонний номер у stream
EventType               наприклад OrderAccepted
SchemaVersion           версія payload contract
OccurredAtUtc           бізнес-час, переданий producer
RecordedAtUtc           час приймання, призначений сервером
CausationId             ID команди/події-причини
CorrelationId           зв'язок workflow
Payload + Headers       валідовані immutable bytes
ClassificationVersion   schema/field lineage для access policy
```

Producer clock не визначає append order. EventId і CorrelationId також не задають порядку між shards. Headers мають size limits, whitelist transport metadata і classification; secrets та raw PII не слід використовувати в назвах stream чи routing keys.

### 38.2. Append і optimistic concurrency

API приймає ExpectedRevision.Exact(n), ExpectedRevision.NoStream або явний ExpectedRevision.Any. Exact порівнює current stream head усередині transaction apply. NoStream вимагає відсутності відповідної stream generation. Any дозволяє append без перевірки бізнес-конкуренції та має бути свідомим вибором клієнта.

Одна транзакція додає весь event batch, змінює head, записує event-ID dedup entries, feed entries та command outcome. Успішний append дає послідовний діапазон revisions. Конфлікт precondition відхиляє весь batch. TailRevision зберігається після retention старих events; наступний append не перевикористовує їхні номери.

Idempotency перевіряється до повторного OCC-рішення: той самий CommandId і payload fingerprint повертає збережений outcome. Повтор EventId з іншим payload дає Conflict. Scope EventId визначається StreamRef; multi-event batch fingerprint включає порядок, headers та expected revision. Retention command/event dedup entries фіксує гарантований retry horizon. Довільний повтор після видалення dedup metadata не отримує обіцянки безстрокової дедуплікації. Змішаний batch із частиною вже наявних EventIds і частиною нових відхиляється як DuplicateEventId; повтор повністю попереднього batch резолвиться за його persisted command outcome. Це усуває неоднозначність часткового append під час retry.

Як reference семантики беремо KurrentDB: .NET API має expected state/revision і atomic append; актуальна документація описує також multi-stream operations. Scope цих операцій у competitor benchmark фіксується окремо від atomic-partition contract KeyLoad. [S48]

### 38.3. ReadStream, catch-up і live tail

ReadStream задає fromRevision, direction, maxEvents, maxBytes та stable committed cut для сторінки. Stream metadata повертає FirstAvailableRevision, TailRevision і retention status. Запит до вже видаленої історії повертає HistoryUnavailable/CursorExpired із дозволеною діагностикою.

Catch-up subscription читає retained records, потім продовжує tail від durable cursor. Для усунення race між останнім scan і реєстрацією wake-up consumer реєструє повідомлення про зміни й повторно перевіряє cursor/tail. Повідомлення про нові дані є підказкою; reconnect і втрачений wake-up відновлюються через повторне читання committed state.

Global feed v1 має стабільну EventSequence у межах AtomicPartitionId. EventSequence записується разом із events і feed index; вона відрізняється від StreamRevision, CommandSequence та Raft LogIndex. Feed cursor через кілька partitions містить vector of positions, partition-discovery epoch і ClusterIncarnation. Під час migration event positions переїжджають із canonical state.

Для широкої subscription через багато entity-scoped atomic partitions cursor передається як opaque server-side handle до persisted position map. На wire повертається bounded token; active map pages і число source partitions мають quota. Query в межах одного stream використовує компактний stream cursor. Server-side map не усуває storage cost checkpointing: E06 вимірює cardinality groups × source partitions. Coarse-grained shard-feed cursor із migration lineage є подальшою оптимізацією і потребує окремого доказу coverage.

Початковий контракт дає порядок у stream і кожному atomic feed. Кластерний total order вимагав би додаткової серіалізації та окремого throughput/availability контракту. Злиття за RecordedAtUtc може бути display order із tie-break; його не можна використовувати як доказ глобального causal/commit order.

### 38.4. Snapshots, schemas і projections

Aggregate snapshot містить StreamRef, SourceRevision, ReducerVersion, StateSchemaVersion, checksum та state. Для rebuild вибирається сумісний snapshot і наступні events. Snapshot не відновлює довільні історичні запити до вже стертих payloads. Retention має окремо описувати, які варіанти replay ще підтримуються.

Upcaster перетворює стару event schema у потрібну in-memory representation під час читання. Оригінальні bytes зберігаються до explicit retention/purge. Reducers та upcasters версіонуються і проходять deterministic fixtures. У першій версії application reducers виконуються у worker-процесі; довільні користувацькі assemblies усередині storage host не завантажуються.

Snapshot/projection write перевіряє очікувану попередню SourceRevision. Для асинхронних моделей зміну read model, inbox receipt і відповідний checkpoint потрібно координувати атомарно в цільовому scope або через idempotent transfer. Події під час rebuild обробляються generation-aware pipeline. Delivery зовнішніх повідомлень із replay вимкнена за замовчуванням; explicit redrive має окремий execution ID та audit.

## 39. Durable queues: claims, leases, ACK і retry

### 39.1. Public queue contract

QueueRef позначає logical queue. QueueLaneRef додає AtomicPartitionId, у якому фізично живе її локальна частина. Consumer читає одну lane або bounded набір lanes за server-side routing. Work queue має один стан виконання для повідомлення; кілька workers конкурують за доступні messages.

```text
Scheduled ── PromoteDue ──> Ready ── Claim ──> Leased ── Ack ──> Acked
                              ▲                 │
                              │                 ├── Nack / ExpireLease
                              │                 │         │
                              └──── retry ──────┘         ↓
                                                     Scheduled

MaxAttempts ──> DeadLettered
Explicit policy ──> Cancelled / Expired
```

At-least-once delivery означає можливість повтору після невідомого outcome, consumer failure або lease expiry. Повторні спроби діють у межах retention, TTL, retry/MaxAttempts policy та доступності кластера. Terminal states і parked messages видимі оператору. Успішне виконання довільного handler не є гарантією storage layer.

Розділяємо publisher commit receipt, delivery ACK receipt і application effect receipt. RabbitMQ документує окремі publisher confirms та consumer acknowledgements; це корисна основа для перевірки наших API boundaries. [S50]

### 39.2. Мінімальний storage layout

Усі ключі мають tenant/AtomicPartition prefix та versioned binary encoding. Event graph namespace E зберігається; для Event Store використовується EV, щоб уникнути конфлікту.

| Namespace | Scoped key | Значення |
|---|---|---|
| SH | streamSet / stream / generation | TailRevision, retention/schema metadata |
| EV | streamSet / stream / generation / revision | Immutable event envelope |
| ED | streamRef / eventId | Fingerprint і committed revision |
| EF | eventSequence / ordinal | EventRef, commit metadata |
| ES | streamRef / sourceRevision / reducerVersion | Aggregate snapshot |
| QB | queue / messageId | Immutable body і classified headers |
| QM | queue / messageId | State, attempts, readySequence, NotBefore, expiry, leaseVersion |
| QR | queue / readySequence / messageId | Ready reference |
| QS | queue / notBeforeUtc / messageId | Scheduled reference |
| QL | queue / leaseUntilUtc / messageId / leaseVersion | Expiry candidate |
| QD | queue / parkedSequence / messageId | Dead-letter reference, sanitized reason |
| SG | subscription / generation | Filter/configuration, source set, group metadata |
| SC | group / sourcePartition | Contiguous checkpoint, bounded gap state |
| SD | group / sourcePartition / eventSequence | Active delivery state |
| IN | handlerScope / executionGeneration / inputId | Inbox receipt і effect outcome |

QR/QS/QL entries змінюються атомарно з QM. Queue body відділяється від часто змінюваного state, щоб ACK/renew не переписував великий payload. Secondary summaries для пошуку активних lanes можна перебудувати; authoritative eligibility перевіряється за QM/QR під writer authority. Для великої кількості lanes потрібні shard-local QXReady(queue, lane) і QXDue(deadline, lane, message, stateVersion) indexes. Вони оновлюються у тому самому local commit, що й canonical lane state. Scheduler/receive використовують їх для bounded seek замість повного обходу всіх AtomicPartitionIds. Migration/recovery перебудовує і перевіряє ці summaries до readiness; stale entries повторно перевіряються при apply. Бюджет справедливого обходу ready lanes і відсутність пропусків перевіряються окремо.

ReadySequence визначає порядок поточної готовності. Після delayed retry повідомлення отримує нову ready position згідно з queue profile. Початковий профіль не обіцяє глобального FIFO через усі lanes. Priority buckets додаються окремим профілем із anti-starvation tests.

### 39.3. ClaimBatch і доставка

Consumer надсилає ReceiveRequestId, maxMessages/maxBytes і lease duration. Server перевіряє права, inflight quota та resource budget, знаходить bounded кандидатів і подає Claim command. Apply повторно перевіряє, що кожен кандидат досі Ready та придатний до delivery. Claim записує current owner, leaseVersion/nonce, attempt і deadline.

Payload і DeliveryToken повертаються після commit із заявленою durability. Multi-lane receive складається з окремих lane outcomes; partial claims чесно відображаються API. Long polling очікує readiness без утримання transaction, writer gate чи довгого grain turn.

Однаковий ReceiveRequestId у встановленому dedup horizon повертає результат тих самих claims; при вже недійсних leases API повертає explicit expired outcome. Загублена відповідь або cancellation клієнта може залишити committed lease до його expiry. Retry без стабільного request ID може створити додаткові inflight claims, що обмежуються quota.

### 39.4. ACK, NACK, renew і fencing

DeliveryToken містить scope tenant/queue/lane/message, principal або authorized consumer identity, leaseVersion, group generation за потреби та ClusterIncarnation. Він має integrity protection. Caller identity перевіряється окремо; token не замінює актуальний permission check.

Ack/Nack/ExtendLease застосовуються тільки до відповідної чинної lease version. Старий worker після redelivery не може підтвердити або змінити lease нового worker. Ідемпотентний повтор уже committed AckCommand повертає попередній outcome в retry horizon, без нового effect. Детальні правила часу наведені у 42.3.

Одна current persisted lease дає одного чинного власника права виконати protected DB commit. Worker із простроченим token може фізично продовжувати працювати. Запобігання його повторним зовнішнім діям потребує application idempotency/reconciliation; receipt fencing у KeyLoad поширюється на власний commit path.

### 39.5. Retry, DLQ, ordering та backpressure

Retry policy задає maxAttempts, base/max delay, exponential factor і jitter. Обраний retry time зберігається в command, щоб replicas відтворили однакове рішення. Негайний нескінченний requeue loop заборонений стандартним профілем. Delivery attempts та business failure reasons обліковуються окремо: втрата claim response теж може витратити delivery attempt.

DLQ у тій самій lane переводить message у terminal state і зберігає parked reference атомарно. Remote DLQ використовує transfer intent із розділу 41.4. При недоступному/повному destination message залишається PendingDeadLetter з захищеним payload; втрата payload заради очищення backlog не дозволяється lossless profile. Redrive потребує explicit admin grant, destination policy check та нової delivery generation.

OrderingKey route-иться в одну lane. StrictPerKey профіль допускає одне активне повідомлення ключа; retry попереднього блокує наступне. Зупинка або park head має explicit Continue/Block policy. Default competing-consumer профіль дає ready-order dispatch у lane; фактичний порядок завершення залежить від workers і retry. Сервер не заявляє упорядкування довільних зовнішніх side effects.

Для мільйонів messages/buckets великі immutable event/queue payloads можна винести у sealed segments після базового benchmark. Такий варіант потребує versioned blob references, checksummed manifests, atomic publication, snapshot/replication integration та crash-safe GC. v0.3 починає зі спільного transactional engine і окремих body/state keys, щоб перевірити базові інваріанти до нового файлового формату.

Queue quotas обмежують stored bytes/messages, inflight bytes/count, receive waiters, attempts і DLQ storage. Control traffic, ACK і renew отримують резерв ресурсу. RabbitMQ quorum queues є specialized benchmark для replication/delivery і роботи з великим backlog; власні KeyLoad semantics фіксуються незалежно. [S51]

## 40. Topics, consumer groups і підписки

### 40.1. Work queue і pub/sub profiles

Для роботи «обробити документ одним worker» використовуємо WorkQueue. Для роботи «кожна підсистема має обробити подію» використовуємо retained Event Stream/Topic плюс незалежні durable groups.

```text
DocumentUploaded
    ├── group: embeddings    → workers 1..N
    ├── group: audit         → workers 1..M
    └── group: notifications → workers 1..K
```

Кожна group має власні delivery states, retry policy і checkpoint. Workers у group розподіляють роботу. Event payload може зберігатися один раз, а groups тримають references та bounded active windows. Фізичне дублювання payload для кожної group не є обов'язковою частиною моделі; retention pins враховують усіх потрібних споживачів.

KurrentDB persistent subscriptions мають server-side checkpoints і competing consumers, включно з ACK/NACK. Власні KeyLoad ordering profiles потрібно перевіряти окремо; прив'язка stream до worker сама по собі не забезпечує порядок за retries. [S49]

### 40.2. Checkpoints і паралельна обробка

Group checkpoint просувається через contiguous acknowledged prefix. Якщо завершені 101 і 103, а 102 ще in-flight, checkpoint не перестрибує 102. Bounded gap bitmap/ranges зберігає завершені positions вище prefix. Коли gaps вичерпали budget, нова доставка зменшується або призупиняється.

Skip через subscription filter є explicit deterministic outcome, прив'язаний до filter generation. Authorization failure для обов'язкових даних зупиняє відповідну delivery/group згідно з policy; мовчазне просування checkpoint повз недоступний event не дозволяється режиму повного replay. Park за configured policy завершує primary attempt sequence і створює audit/park record; replay parked items має окремі semantics.

Group membership/rebalance має persisted generation/epoch. Після ownership change старі delivery tokens не підтверджують нові assignments. Revocation перевіряється для наступної доставки, сторінки та ACK-команди. Application service account групи отримує явно визначений data scope.

### 40.3. Start positions і partition discovery

CreateSubscription задає FromBeginning, FromNow або FromCursor. FromNow фіксує cut поточних source partitions разом із discovery epoch. Записи, що з'явилися після cut, доступні через catch-up; нові partitions реєструються у coverage map з визначеної початкової позиції. Resume cursor включає source coverage, тому topology change не губить новостворені lanes.

Зміна predicate/source set створює нову subscription generation або явну migration. Group, створена пізніше, може replay тільки доступну за retention історію. CursorExpired/HistoryUnavailable містить дозволену інформацію про earliest position; автоматичний стрибок на tail вимагає explicit opt-in.

Cross-partition subscription повертає vector of checkpoints. Consumer може налаштувати per-stream/per-key порядок та власну causal logic. Загальний global counter на кожну event mutation до базової архітектури не додається.

### 40.4. CDC і внутрішні projections

Resumable CDC із KL-079 використовує захищений committed change feed. Бізнес-події Event Store мають власний public contract. Internal index workers читають системний outbox із reserved consumer identity; відключення публічних queues не зупиняє обов'язкове підтримання scalar/text/vector projections.

Для projection записується origin/reducer identity та source position. Source filters не включають власні derived mutations за замовчуванням, щоб projection не створювала нескінченний цикл обробки власних змін. Автоматичний routing events → jobs дозволений лише для versioned declarative rules з authorization, quotas та bounded fanout.

## 41. Єдина транзакція для документів, events і messages

### 41.1. TransactionDomain і catalog binding

Попередня mapping tenant + collection + partitionKey розширюється явним TransactionDomainId. Domain об'єднує resources, які мають право співрозміщуватися і брати участь у спільному batch. Для resources поза shared domain каталог може створити isolated domain, що зберігає попередню поведінку.

```text
AtomicPartitionId = Resolve(
    TenantId,
    DatabaseId,
    TransactionDomainId,
    VersionedPartitionKey)
```

Collections, stream sets та queues посилаються на domain у catalog. Однаковий рядок `order-42` у двох unrelated resource definitions не дає права на спільний commit. Клієнт передає domain reference; сервер перевіряє кожен resource binding, тип partition key, scope principal та routing epoch.

У прикладі Domain `order-processing` об'єднує collection `orders`, stream set `order-events` і lanes queues `send-email`/`enrich-order`. PartitionKey `order-42` визначає один AtomicPartitionId для всіх цих resources. Physical shard може пакувати багато таких order partitions. Створення окремого engine або grain для кожного order не потрібне.

### 41.2. Producer atomic batch

```text
Authorize all operations + resolve one AtomicPartitionId
                            ↓
                 Dedup CommandId / fingerprint
                            ↓
                     Ordered commit/apply
                            ↓
  Document + strict indexes + explicit domain events
  + stream heads + event feed + queue body/ready state
  + projection outbox + outcome + apply position
                            ↓
                 Required persistence barrier
                            ↓
                       CommitReceipt
```

Preconditions усіх mutations перевіряються в одному atomic scope. Невдала stream revision, document CAS, permission або queue quota відхиляє batch. Зміни одного canonical batch стають видимими разом за перевіреним read-view contract. Text/ANN projections мають власні watermarks і можуть відставати.

Queue lane є частиною того самого atomic partition. Global consumer discovery агрегує lanes асинхронно, а periodic catch-up забезпечує їхню знаходжуваність після втрати notification. Local queue acceptance у CommitReceipt означає committed message state. External handler completion повертається окремою подією/receipt.

### 41.3. Consumer atomic effects

CommitProcessing приймає DeliveryToken, HandlerScope/ExecutionGeneration та bounded набір database mutations. У transaction перевіряються current lease, input identity, inbox receipt і всі preconditions; потім зберігаються documents/events/outgoing messages, inbox outcome та ACK. Усі records повинні знаходитися в одному AtomicPartitionId.

Після втрати відповіді retry з тим самим processing command повертає outcome. Повторна доставка обробленого InputId не застосовує ці database effects повторно в заявленому inbox horizon. Формальна гарантія стосується **одного committed effect для конкретного input у визначеній transactional області та dedup horizon**. CPU handler може виконуватися більше одного разу.

Для input із іншого partition цільова транзакція атомарно зберігає inbox + effects; source ACK виконується після її commit. Crash між цими кроками спричиняє redelivery, яку зупиняє target inbox. Ця послідовність має окремий статус eventual source acknowledgement. Усі output intents, які повинні пережити handler crash, записуються разом із target effects.

Зовнішні виклики, зокрема email, оплата чи сторонній HTTP API, виконуються поза database transaction. Для них використовуються стабільний provider idempotency key, persisted intent/result і reconciliation. Коли receiver не підтримує потрібної дедуплікації, API/UI явно показують можливість duplicate/unknown outcome. KeyLoad ACK не доводить успіх будь-якої зовнішньої дії.

### 41.4. Remote queues і transfers

Для output у іншому atomic partition source transaction додає TransferIntent із deterministic TransferId, destination reference, schema/policy version і payload або pinned reference. Dispatcher робить ідемпотентний destination enqueue з тим самим TransferId. Target receipt зберігається локально, після чого source позначає intent Delivered.

Source commit receipt показує OutputPending. Передача може бути повторена після timeout; destination dedup horizon має покривати весь можливий source retry/redrive window. Для stronger lifecycle до explicit source release можна зберігати transfer tombstone; нескінченний retention такого state потребує quota/GC contract.

Reference payload не може бути видалений до завершення transfer або explicit audited cancellation. Destination permission/schema failure паркує transfer з видимим outcome. Присутність resources в одному фізичному engine не розширює public atomic-partition scope. Cross-partition bulk transfer має per-destination outcomes.

## 42. Orleans, workers і persisted scheduling

### 42.1. Ролі runtime

```text
API / C# SDK
      ↓
Event/Queue Commands + Query Frontends
      ↓
Authorization + Domain Resolver + Resource Governor
      ↓
Orleans routing / partition coordination
      ↓
Replica-local PartitionHost + ordered commit + ZoneTree
      ↓
Durable cursor/lease state → external worker delivery
```

Orleans stream providers визначають поведінку відповідно до backing queue; Microsoft прямо описує втрату memory-stream messages при silo restart. Для KeyLoad durable delivery базується на власному persisted state і qualified replication profile. [S52]

Orleans окремо документує delivery guarantees для grain calls. Persisted application command identity та dedup у KeyLoad зберігаються незалежно від transport retry behavior. [S53]

Усередині KeyLoad пропонуються QueueShardCoordinator, SubscriptionGroupCoordinator та SchedulerShardCoordinator. Вони працюють з багатьма lanes/streams, швидко координують batches і не тримають відкриту database transaction під час handler виконання. Grain activation є execution context; source of truth для claims/checkpoints залишається у storage.

### 42.2. Consumer execution

User handlers працюють у .NET Worker Service, ASP.NET застосунку або іншому клієнті SDK. Trusted власні host modules можуть обробляти системні проєкції під окремими capability grants. Довільні assemblies із запиту клієнта в server process не завантажуються.

Optional SessionGrain містить subscriptions і client tokens; незалежні receive requests та handler executions мають bounded concurrency. Для кожного message не створюється окремий durable actor. Workers можуть масштабуватися незалежно від storage nodes; вони підключаються через endpoint discovery і отримують lanes за чинними permissions.

SDK helper виконує receive → handler → renew за потреби → CommitProcessing/Ack, з cancellation і unknown-outcome resolution. Helper не повинен автоматично повторювати неідемпотентний зовнішній side effect. Handler idempotency та external action contract задаються застосунком.

### 42.3. Scheduler, lease time і failover

NotBeforeUtc, LeaseUntilUtc, ExpiresAtUtc та retries знаходяться у persisted records. Bounded heap або timing wheel у пам'яті зберігає найближчі deadlines; після restart його відновлюють із QS/QL range scans. Один scheduler на shard/bounded shard group може керувати багатьма messages. Timer/reminder лише ініціює перевірку durable due records.

Leader пропонує PromoteDue/ExpireLease/ExpireMessage commands із recorded evaluation time та expected state/lease version. Followers застосовують передані значення детерміновано. ACK/renew commands теж перевіряються за authoritative evaluation time, чинною lease version та deadline. Пряма перевірка wall-clock кожною replica всередині apply не допускається.

Persisted last-observed scheduling time та monotonic runtime clock обмежують зворотний рух у межах дозволеної конфігурації. Після leader change scheduler проходить clock-sanity/readiness gate. Значний drift або невизначеність часу призупиняє залежні від deadline операції згідно з policy. Physical-time punctuality потребує задокументованої clock uncertainty та availability assumptions; точний момент запуску до мілісекунди не заявляється.

ExpireLease command містить очікувану lease version і state revision/deadline. Конкурентне renew, яке вже committed, робить стару expiry-команду застарілою. Renew повертає новий authoritative deadline; consumer використовує запас часу. Cancellation або закінчення lease не зупиняє CPU чужого процесу, тому protected effects перевіряють fencing.

### 42.4. Resource isolation і modular deployment

Control plane, commit, ACK/renew, delivery, queries, reindex та compaction мають окремі concurrency/IO budgets. Prefetch має строгий memory budget. Backlog лишається на диску; active window обмежується. При backlog over quota producer отримує ResourceExhausted до commit.

Одновузловий all-in-one deployment має спільний failure domain. Для великих workloads catalog може призначати dedicated physical shard pools для messaging, event history або search-heavy tenants. Рознесення resources, які були у спільному atomic domain, потребує збереження co-location або explicit переходу до transfer protocol.

Feature manifest розділяє Documents, EventStore, WorkQueues, Topics, Scheduler, Graph, TimeSeries, Search. Internal recovery/outbox workers є частиною kernel. AMQP/Kafka/Redis wire compatibility та adapters до сторонніх .NET bus frameworks мають власні qualification tasks після стабілізації базового протоколу.

## 43. SQL, SDK та наскрізні сценарії

### 43.1. Producer API

Наступний фрагмент є **ескізом майбутнього API KeyLoad**. Типи, назви методів та namespaces ще підлягають реалізації. Всі resources прикладу заздалегідь прив'язані catalog до domain `order-processing`.

```csharp
// Ескіз API. Batch будується локально до одного CommitAsync.
var partition = client.ForPartition(
    transactionDomain: "order-processing",
    partitionKey: orderId);

var batch = partition.CreateBatch(commandId);

batch.Documents.Put(
    collection: "orders",
    id: orderId,
    value: updatedOrder,
    expectedRevision: documentRevision);

batch.Events.Append(
    streamSet: "order-events",
    streamId: $"order/{orderId}",
    events: new[] { orderAcceptedEvent },
    expectedRevision: ExpectedRevision.Exact(streamRevision));

batch.Queues.Enqueue(
    queue: "send-email",
    messageId: messageId,
    payload: new { OrderId = orderId, Template = "accepted" });

var receipt = await batch.CommitAsync(cancellationToken);
```

EventId, MessageId та CommandId створюються один раз для логічної операції й зберігаються для retry. DocumentRevision та StreamRevision незалежні. Network timeout повертає UnknownWriteOutcome; перевірка outcome або retry використовує той самий CommandId. Builder не відкриває server-side transaction на весь час створення об'єкта.

Consumer API primitives: ReceiveBatch, Ack, Nack, ExtendLease, GetDeliveryOutcome, CommitProcessing. Management API: CreateQueue, PauseQueue, ResumeQueue, InspectMessages, ReadDeadLetters, Redrive, CreateSubscription, GetLag, SeekSubscription. Operations мають scoped permissions і idempotent command IDs там, де відбувається mutation.

### 43.2. SQL для читання подій і queue diagnostics

Проєктна граматика розширює спільний authorized AST:

```sql
SELECT e.revision, e.eventType, e.recordedAt, e.payload
FROM EVENTS(@streamRef) AS e
WHERE e.revision > @afterRevision
ORDER BY e.revision
LIMIT 100;
```

```sql
SELECT m.messageId, m.state, m.attempts, m.notBefore
FROM QUEUE_MESSAGES(@queueRef) AS m
WHERE m.state = 'DeadLettered'
ORDER BY m.messageId
LIMIT 50;
```

EVENTS резолвить StreamRef і stream-read policy. QUEUE_MESSAGES надає authorized operational view; delivery tokens, secrets та raw body виключені без відповідного grant. SELECT має read-only semantics і не змінює claims/checkpoints. Для отримання права виконати задачу використовується explicit Receive/Claim command.

Generic UPDATE/DELETE по internal stream, queue state чи checkpoints закриті. Окремі майбутні SQL statements APPEND/ENQUEUE можуть компілюватися в ті самі authorized commands; вони не створюють альтернативний шлях обходу invariants. DDL/capability validation однакова для HTTP/SQL/SDK.

### 43.3. Event → job → document/vector → пошук

Приклад для knowledge pipeline:

```text
Commit A, один domain partition:
  document status=uploaded
  + event DocumentUploaded
  + queue job GenerateEmbedding
                    ↓
Worker: model inference поза database transaction
                    ↓
Commit B, той самий domain partition:
  verify input revision + delivery token
  + canonical vector + processing status
  + event EmbeddingGenerated + inbox receipt + ACK
                    ↓
Committed outbox → ANN/text projection → search watermark
```

Якщо документ змінився під час inference, Commit B відхиляє stale input revision. Retry/cancellation policy визначає, чи потрібна нова job. Output для іншого partition використовує idempotent effect protocol з 41.3. Модель і дозволені source fields зафіксовані в job metadata, щоб PII не потрапила до provider без grant.

EmbeddingGenerated означає committed canonical result. Видимість у ANN визначає WaitForIndex(receipt.Token). Сам факт ACK job не використовується як заміна search freshness barrier. Цей сценарій поєднує database, Event Store, work queue та hybrid search у перевірюваний workflow.

### 43.4. Наступні можливості

Delayed jobs, retries, recurring schedules із explicit timezone/misfire policy, saga state та timeout messages можна будувати на цьому kernel. Recurring occurrence має stable OccurrenceId для dedup. General durable workflow engine із replay користувацького коду, activity history та version-safe orchestration є окремою capability; відповідні guarantees мають власні tests.

Event payloads можна індексувати для text/vector search за opt-in profile. Result identity буде EventRef; graph/time-series projections зберігають provenance до нього. Автоматичне повнотекстове й векторне індексування всіх queue payloads відсутнє в default configuration через storage/CPU/privacy budgets.

## 44. PII, retention, backup і recovery для eventing

### 44.1. Права та masking

Нові capabilities: events.append/read/replay, streams.manage, queue.publish/consume/inspect, queue.ack/renew, subscriptions.manage, deadletters.read/redrive та scheduler.manage. Кожна має tenant/database/resource scope. Publish grant не дає автоматичного права читати backlog; diagnostics grant не дає права отримати delivery token.

Event schema й message schema мають classification catalog, source lineage та current output policy. Rules застосовуються до payload, headers, SQL views, subscriptions, DLQ, export, tracing та client error text. Classification data зберігається з поколінням schema, а актуальні restrictive overrides враховуються при наступній delivery/read boundary.

Для human inspection використовується safe projector. Для reducer/worker, якому обов'язково потрібні raw fields, capability оголошує required data scope; брак дозволу дає PermissionDenied/ParkedByPolicy. Мовчазна заміна необхідного input на redacted JSON може змінити бізнес-результат, тому такий fallback не застосовується автоматично.

За можливості event містить CustomerRef і business facts, а mutable contact data живе у захищеному документі. Архіви, backup і replicas зберігають свій raw-data threat model. Omit у JSON response не означає стирання bytes із history.

### 44.2. Retention і privacy purge

Event streams можуть мати KeepAllUntilQuota або explicit bounded retention. KeepAllUntilQuota відхиляє новий append при вичерпанні бюджету. Bounded retention оголошує earliest replay revision і правила snapshot/archive. Work queues мають retention terminal receipts і backlog policy; topics мають незалежний lifetime retained events.

Slow consumer може pin-ити необхідну історію в межах встановленої quota. При досягненні межі policy обирає pause ingestion, explicit consumer failure/required resync або approved discard profile. Тихе стирання unread events із одночасним successful checkpoint заборонене. Active rebuild, transfer reference та durable subscription враховуються retention coordinator.

Privileged Erase/Purge workflow може видалити конкретні classified payloads із events, queues, DLQ та derived data із збереженням мінімального authorized tombstone/audit. Stream positions зберігають свою ідентичність; replay повідомляє RedactedEvent/HistoryUnavailable відповідно до contract. Hash старого payload сам може бути sensitive; його збереження має окрему policy.

Повне physical purge враховує WAL, replicas, compaction, snapshots, backups, external archives і ключі шифрування. Цей дизайн не встановлює автоматичної юридичної відповідності; deployment має документувати реальні строки та межі видалення. Crypto-erasure потребує окремого key-lifecycle design і перевірки всіх copies.

### 44.3. Що входить у backup

Authoritative manifest охоплює domain bindings, stream generations/heads, event records та dedup, stable event sequences, queue bodies/states/ready/scheduled/lease indexes, group configs/checkpoints/gaps, inbox receipts, transfer intents та their retention pins. Rebuildable indexes мають validation/rebuild recipe. Повна stream history не відновлюється з Raft snapshot, якщо її canonical records не були включені.

Для offline backup усі ці records входять у той самий закритий storage cut. Cluster backup зберігає catalog epoch та per-partition cuts. Cross-partition transfer reconciliation має перевірити source intent, destination dedup і їхні cut boundaries. Зовнішні side effects до snapshot не відкочуються разом із базою.

### 44.4. Restart, failover і restore старого cut

Після звичайного restart сервер відновлює committed queue state та checkpoints до нових claims. Незавершені leases проходять deterministic deadline/ownership evaluation. Після leader loss тільки нова qualified authority видає delivery і приймає ACK; duplicates лишаються допустимою поведінкою at-least-once profile.

Restore старішого backup стартує з **delivery paused** та новою ClusterIncarnation. Старі delivery tokens/cursors інвалідуються. MessageIds/InputIds зберігаються для reconciliation, а відновлені leases позначаються для контрольованої re-evaluation перед resume. Користувач переглядає ризик redelivery повідомлень, які були успішно оброблені після backup cut, і явно відновлює dispatch.

Paused restore захищає від автоматичного повторного надсилання листів, викликів API та інших необоротних дій. SDK/operations guide пояснює, як отримати provider outcomes за idempotency keys. Без зовнішніх receipts повна реконструкція фактично виконаних дій може бути недоступна.

Shutdown спочатку зупиняє нові claims, залишаючи бюджет на ACK/renew/drain; потім закриває writers/readers і файли. Abrupt crash завжди перевіряється окремо від graceful shutdown. Підтверджений durable ACK не redeliver-иться у тій самій incarnation за normal operation; explicit redrive і restore минулого cut мають окремі правила.

## 45. Продуктивність, конкурентні baselines і оновлений roadmap

### 45.1. Performance hypothesis та вартість гарантій

Гіпотеза KeyLoad: co-located document/event/queue batch зменшить кількість application round trips і duplication of coordination для змішаних workflows. Ordered seeks, small delivery state, batching та shared authorization можуть скоротити resource cost. Перевага ще потребує вимірювань.

Повний queue lifecycle зазвичай включає persisted enqueue, claim і ACK, а також renew/retry transitions за потреби. Group commit може об'єднати flush кількох operations; це не скасовує replication, indexing і log bytes. Throughput тільки producer-side accepted messages не використовується як completed-work throughput.

Вимірюються publish commit p99, claim p99, durable ACK p99, ready-to-start latency, scheduled lateness, unique completed jobs/s, stream append/read/replay throughput, consumer lag, gap-memory, bytes/message, write amplification, dedup retention cost і impact на document/search p99. Handler CPU/external API time вимірюється окремо та в наскрізному workflow.

### 45.2. Baselines

PostgreSQL baseline включає indexed event rows `(streamId, generation, revision)`, stream head/CAS, queue lease state, inbox та outbox у звичайних transactions. Для competing consumers використовується коротка транзакція SELECT FOR UPDATE SKIP LOCKED → update claim → COMMIT. PostgreSQL прямо документує SKIP LOCKED як механізм для queue-like contention. Handler виконується після commit claim, без довгого row lock на весь business workload. [S56]

**Marten + Wolverine** є обов'язковим .NET combined baseline: Marten працює з documents/events, інтеграція Wolverine зберігає outgoing messages до dispatch після commit. Wolverine має PostgreSQL durability integration та database-backed messaging. Ця наявна інтеграція означає, що перевагу KeyLoad на одному transactional workflow треба довести практично. [S54][S55]

RabbitMQ quorum queues додаються як specialized messaging arm, KurrentDB як specialized append/replay/subscriptions arm. [S48][S49][S51] Specialized arm порівнюється тільки за заявленим спільним workload. PostgreSQL synchronous replication, KeyLoad RF3 і competing systems отримують явно зафіксовані durability/failure assumptions. Результат on-disk queue порівнюється зіставно з on-disk queue.

### 45.3. Workload matrix для Event Store і messaging

| ID | Сценарій | Що перевірити |
|---|---|---|
| E01 | Append batch 1/32/128; багато streams і один hot stream | p99, OCC conflicts, no duplicate event versions |
| E02 | Stream forward/backward read, catch-up → live, >RAM replay | Throughput, missing/duplicate events, bounded memory |
| E03 | Producer enqueue → receive → ACK; 128 B/1 KiB/16 KiB bodies | Unique completed throughput, durable receipts |
| E04 | Worker crash, lost receive/ACK response, slow expired worker | Redelivery, receipt fencing, inbox effects |
| E05 | Delayed jobs, clock skew, restart біля deadline, renew race | Lateness, no premature claim за declared clock bound |
| E06 | 1/10/100 groups; slow group і out-of-order completion | Contiguous checkpoint, fanout IO, gap/cache budget |
| E07 | Document + event + local enqueue, producer і consumer batches | Atomic invariants, PG/Marten/Wolverine comparison |
| E08 | Cross-partition transfer, duplicate deliveries, remote DLQ full | No lost intent/payload, bounded retry, visible terminal status |
| E09 | 100k/1M/larger-than-RAM backlog під ANN/reindex/compaction | Database p99, control-plane/ACK liveness, IO fairness |
| E10 | Retention pin, privacy purge, pause/restore/resume | CursorExpired, redaction, no automatic external replay |
| E11 | Node loss, total reset fault model, lane/shard movement | Acknowledged durable outcomes і preserved domain scope |

Числа позначають test configurations. Вони не є виміряними capacities. Open-loop load, timeout/reject rates, hardware/versions, datasets, comparable policy overhead і raw samples фіксуються за правилами розділу 32. Primary success target задається до runs; messaging gains не поширюються автоматично на SQL або ANN workloads.

### 45.4. Оновлений порядок реалізації

| Хвиля | Результат | Gate |
|---|---|---|
| F0 | Storage qualification, TransactionDomain, stream/queue contracts, PG/.NET baseline plan | Exact atomicity/ack/retention semantics |
| F1 | Documents + strict indexes + append/revision + enqueue/claim/ACK + retry + SQL + PII | Producer atomic batch і crash/retry invariants |
| F2 | Inbox + CommitProcessing, delayed scheduler, durable groups/checkpoints, ранній RF3 spike | No duplicate protected effects, no skipped inputs |
| F3 | Graph/TS + BM25/exact/ANN, event-driven projections | Provenance, freshness, quality та mixed-load benchmark |
| F4 | Remote transfers, rebalance, shard movement, resource isolation | Replicated recovery, durable cursor/lease migration |
| F5 | Full restore drills, adversarial security, endurance і release manifest | Advertised capabilities мають real-process evidence |

Складні search operators розробляються після мінімального event/queue kernel; interfaces для них залишаються в архітектурі. F0/F1 уточнюють порядок ранніх задач P0/P1. Попередні P2..P6 dependency chains продовжують діяти для відповідних capabilities. Optional sagas/recurring orchestration не блокують базовий queue release, якщо capability manifest їх не заявляє.

Перший новий demonstrator: один commit змінює Order, додає OrderAccepted і SendEmail job; worker отримує lease, записує результат та ACK атомарно; process crash і lost response не порушують database/inbox invariants. Реальне надсилання email має окрему provider idempotency test fixture.


## 46. Додатковий backlog KL-081..KL-104 та ADR

Ці 24 задачі доповнюють попередні 80. Labels F0..F5 визначають integrated slices, а P0..P6 зберігають фази попереднього backlog. Multi-phase test ticket має ранню локальну acceptance частину та пізні capability-specific cases; граф залежностей означає повне завершення ticket. Раннє тестування не очікує появи всього кластера.

### KL-081 · Eventing contracts і shared TransactionDomain

**Етап:** P0. **Залежності:** KL-002, KL-045.

**Робота:** Зафіксувати domain binding, atomic producer/consumer scope, stream revisions, delivery guarantees, retry/retention horizons та stable errors.

**Приймання:** Кожен resource резолвиться у server-verified atomic scope; однаковий literal key різних domains не зливається; compatibility fixtures для попереднього isolated-domain routing збережені.

### KL-082 · Event/queue keyspace і state envelopes

**Етап:** P1. **Залежності:** KL-007, KL-008, KL-081.

**Робота:** Додати SH/EV/ED/EF/ES, QB/QM/QR/QS/QL/QD, SG/SC/SD/IN; metadata для state versions і classified payloads.

**Приймання:** Golden ordering/roundtrip tests пройдено; graph E namespace незмінний; ready/scheduled/lease indexes мають rebuild/validation recipe; body buffers immutable.

### KL-083 · AppendEvents, expected revision та dedup

**Етап:** P1. **Залежності:** KL-009, KL-012, KL-082.

**Робота:** Atomic event batches, stream head, Exact/NoStream/Any, EventId/fingerprint і persisted outcomes.

**Приймання:** Concurrent Exact append має одного committed переможця; retry не дублює batch; same ID/different payload дає Conflict; head і events узгоджені після seeded crashes.

### KL-084 · ReadStream, feed cursors і catch-up

**Етап:** P1/P3. **Залежності:** KL-004, KL-013, KL-083.

**Робота:** Bounded forward/backward reads, stable per-atomic EventSequence, vector cursor, catch-up → tail без lost wake-up.

**Приймання:** Reader бачить тільки committed batch; race append/register не губить event; gaps через retention explicit; restart/reconnect зберігає доступні events.

### KL-085 · Aggregate snapshots, schemas та replay

**Етап:** P3. **Залежності:** KL-084, KL-064.

**Робота:** SourceRevision/ReducerVersion snapshots, versioned upcasting, pure external-worker replay і projection concurrency.

**Приймання:** Snapshot+tail відтворює reference state; incompatible reducer snapshot відхиляється; erased/missing history explicit; replay за замовчуванням не запускає external outputs.

### KL-086 · Durable enqueue і queue-lane state

**Етап:** P1. **Залежності:** KL-012, KL-082.

**Робота:** QB/QM atomic insert, ready/scheduled indexes, message identity/fingerprint, lane quotas та terminal metadata.

**Приймання:** Enqueue retry дає один message у horizon; duplicate ID/different payload rejected; quota failure відхиляє producer batch; crash не відділяє payload від state.

### KL-087 · Claim, ACK, renew і delivery fencing

**Етап:** P1. **Залежності:** KL-003, KL-086, KL-096.

**Робота:** ReceiveRequestId, bounded ClaimBatch, persisted lease token, ACK/NACK/renew preconditions і outcome lookup.

**Приймання:** Delivery відбувається після потрібного commit; lost claim/ACK response recovery пройдено; stale token не впливає на нову lease; multi-lane response чесно показує partial outcomes.

### KL-088 · Retry, DLQ, cancellation та expiry

**Етап:** P1/P3. **Залежності:** KL-087.

**Робота:** Versioned state machine, backoff/jitter, MaxAttempts, parked state, redrive generation та StrictPerKey profile.

**Приймання:** Кожен перехід має reference fixture; full DLQ зберігає pending payload; retries bounded; stale expiry/renew race без double state; ordering-profile limitations задокументовані.

### KL-089 · Durable groups і contiguous checkpoints

**Етап:** P3. **Залежності:** KL-084, KL-087.

**Робота:** Independent groups, competing workers, gap bitmap/ranges, filter generations, coverage discovery і ownership epochs.

**Приймання:** ACK 103 не просуває checkpoint повз pending 102; bounded gaps дають backpressure; reconnect/filter change/new partition не створюють тихих пропусків; retained history replayable.

### KL-090 · Inbox і CommitProcessing

**Етап:** P1/P3. **Залежності:** KL-012, KL-087, KL-091.

**Робота:** Handler/input dedup, protected atomic effects, local ACK в тому самому batch та cross-partition target-inbox pattern.

**Приймання:** Crash після effect commit до response не повторює DB effects; stale delivery не commit-ить local batch; external calls винесені за transaction; dedup horizon і redrive identity explicit.

### KL-091 · Document + event + queue atomic batch

**Етап:** P1. **Залежності:** KL-010, KL-011, KL-083, KL-086, KL-081.

**Робота:** Розширити batch compiler/apply для shared domain resources, усіх preconditions, queue quotas, outbox та committed receipt.

**Приймання:** Failure кожної operation відхиляє весь batch; після crash усі canonical частини одного commit присутні разом; unrelated domains rejected; ANN freshness відображена окремо.

### KL-092 · Persisted scheduler і clock contract

**Етап:** P3. **Залежності:** KL-088, KL-052.

**Робота:** QS/QL seek, bounded deadline heap, authoritative evaluation time, logged transitions, drift/readiness gate і shutdown order.

**Приймання:** Restart відновлює schedules; followers застосовують ті самі time decisions; clock-skew і lease-renew races покриті; no per-message timer allocation; lateness виміряно.

### KL-093 · Orleans coordinators і worker SDK

**Етап:** P2/P3. **Залежності:** KL-053, KL-087, KL-089, KL-092.

**Робота:** Shard-level queue/group/scheduler coordinators, external worker receive/renew helpers, auth context і bounded long polling.

**Приймання:** Silo/worker restart не губить persisted delivery state; session head-of-line blocking відсутній; handlers не виконують network IO під storage gate; memory inflight quotas дотримано.

### KL-094 · Cross-partition transfer outbox

**Етап:** P5. **Залежності:** KL-016, KL-020, KL-086, KL-090.

**Робота:** TransferId, source intent, destination dedup receipt, retry, remote DLQ і retention-reference pins.

**Приймання:** Crash на кожному етапі не губить intent/payload; повтор target enqueue безпечний; source показує OutputPending до receipt; destination failures visible; source/dedup horizons узгоджені.

### KL-095 · Event SQL, queue views та protocol

**Етап:** P1/P3. **Залежності:** KL-051, KL-084, KL-087, KL-064.

**Робота:** Authorized EVENTS/QUEUE_MESSAGES operators, stream/queue SDK contracts і explicit mutation endpoints.

**Приймання:** SQL/JSON/SDK мають equivalent permissions; SELECT не claim-ить messages; internal state неможливо змінити generic UPDATE; views не розкривають lease tokens.

### KL-096 · Event/message RBAC і sensitive schemas

**Етап:** P1. **Залежності:** KL-061, KL-062, KL-064, KL-081.

**Робота:** Publish/consume/replay/inspect/redrive permissions, header/body classifications, required-input grants та DLQ policy inheritance.

**Приймання:** Unprivileged inspection omits PII; worker із браком required field access отримує явний outcome; schema reclassification/revocation поширюється на наступні deliveries; secrets не логуються.

### KL-097 · Event-driven projections і search lineage

**Етап:** P4. **Залежності:** KL-085, KL-029, KL-031, KL-067.

**Робота:** EventRef provenance, uploaded → job → vector pipeline, reducer generations, input revision guards та WaitForIndex.

**Приймання:** Stale inference result не перезаписує новий document state; replay не запускає side effects за замовчуванням; projection не читає власні mutations нескінченно; PII lineage збережено.

### KL-098 · Eventing retention, purge і backup state

**Етап:** P3/P5. **Залежності:** KL-005, KL-085, KL-088, KL-089.

**Робота:** Consumer/rebuild/transfer pins, history availability, receipt horizon, event/queue raw backup manifest та paused restore.

**Приймання:** Unread/history loss ніколи не маскується successful resume; source positions лишаються стабільними; restore відновлює group/inbox/queue invariants із paused dispatch; remote-transfer cases активуються разом із KL-094.

### KL-099 · Eventing replication і migration

**Етап:** P5. **Залежності:** KL-035, KL-036, KL-072, KL-087, KL-089.

**Робота:** Перенесення domain bindings, event sequence, queue leases, subscriber gaps, delivery generations і recovery readiness.

**Приймання:** Старий owner не видає нові claims; committed ACK збережені в normal failover; stream cursor не порівнює різні Raft logs; atomic domain переїжджає зі всіма resources.

### KL-100 · Delayed jobs, recurring occurrences і saga state

**Етап:** P5, optional. **Залежності:** KL-090, KL-092, KL-094.

**Робота:** Versioned schedules, timezone/misfire rules, OccurrenceId, saga CAS і timeout messages у межах заявленого processing contract.

**Приймання:** Duplicate timer delivery не створює duplicate occurrence; restart preserves saga state; cancel/timeout race explicit; general workflow-engine compatibility не заявляється без окремого тестування.

### KL-101 · Eventing benchmark kit і .NET baselines

**Етап:** P0/P1..P6. **Залежності:** KL-006, KL-073, KL-081.

**Робота:** E01..E11, PG event/lease schemas, Marten/Wolverine integration, optional specialized KurrentDB/RabbitMQ arms.

**Приймання:** Durability/quality/security/resource assumptions збережені; receive/ACK і unique-completion metrics відділені від enqueue; перед runs обрано primary target; actual results only після появи implementation.

### KL-102 · Eventing failure і state-machine suite

**Етап:** P1..P5. **Залежності:** KL-091, KL-090, KL-088, KL-099.

**Робота:** Lost responses, expired slow workers, ACK races, checkpoints gaps, process kill, partitions, total-reset model і restore-old-cut.

**Приймання:** Ранній local gate активний з KL-091/KL-090/KL-088; cluster cases додаються з KL-099; forbidden partial mutations/duplicate protected effects/скіпи pending input блокують реліз відповідної capability.

### KL-103 · Eventing adversarial security suite

**Етап:** P1..P5. **Залежності:** KL-096, KL-095, KL-097.

**Робота:** Tenant/token forgery, role revoke, headers/body/DLQ leaks, replay із redacted inputs, unsafe export і arbitrary-handler injection.

**Приймання:** Local delivery security gate виконується з KL-095/KL-096; search-lineage cases додаються з KL-097; канаркове PII відсутнє в недозволених outputs; required-input policy failures explicit.

### KL-104 · All-in-one release manifest і runbooks

**Етап:** P6. **Залежності:** KL-080, KL-098, KL-099, KL-101, KL-102, KL-103.

**Робота:** Єдиний release gate для declared data/event/messaging capabilities, restores, limits, latency, operator runbooks і dependency provenance.

**Приймання:** Кожен advertised contract має real-process evidence; paused restore та redrive runbook перевірені; optional KL-100 потрібен лише для заявленої job/saga capability; непідтверджені performance claims відсутні.

### ADR-023..ADR-031

| ADR | Рішення | Критерій перевірки |
|---|---|---|
| ADR-023 | Domain events, CDC, WAL і queue state мають окремі lifecycle/authority | Trim одного journal не руйнує public history contract |
| ADR-024 | Shared TransactionDomain через catalog resource binding | Atomic batch однаково резолвиться до/після movement |
| ADR-025 | Stream revision + stable per-atomic event feed positions | Cursor vector і retained history коректні після restart/migration |
| ADR-026 | At-least-once delivery, fenced leases, inbox і protected DB effects | Lost ACK/claim response та expired-handler tests |
| ADR-027 | Durable groups із contiguous checkpoint і bounded gaps | Parallel completion не пропускає pending input |
| ADR-028 | Persisted scheduling; logged time evaluation та clock sanity | Replicas/restart дають однакові transition outcomes |
| ADR-029 | Event/message classification, required-input grants, safe diagnostics | PII/token canaries, denial перед unsafe replay |
| ADR-030 | Retention pins і paused restore із новою incarnation | No automatic external redelivery після restore старого cut |
| ADR-031 | Modular all-in-one deployment, reserved control resources | Mixed-load liveness та чесний capability/protocol manifest |

### Підсумковий контракт редакції 0.3

KeyLoad надає одне кероване середовище для поточного стану, бізнес-історії та надійного запуску обробки. Спільний catalog domain дозволяє документам, stream events і local queue state commit-итися разом. Розподілені transitions, side effects, retention та search freshness мають явні межі. Перший кодовий результат має довести ці інваріанти на мінімальному vertical slice; масштабування та перевага над PostgreSQL/Marten/Wolverine підтверджуються наступними тестами.

## Джерела

S1..S45 збережені з дослідження редакції 0.2 від 16 вересня 2026 року; S46..S56 використані при розширенні 0.3 того самого дня. Сторінки main та online docs змінюються; release engineering має зафіксувати commits/package versions перед реалізацією. Публічна документація описує capabilities; production qualification виконується задачами P0/P2/P6.

[S1] ZoneTree repository і README: https://github.com/ZoneTree/ZoneTree

[S2] ZoneTree transactions: https://github.com/ZoneTree/ZoneTree/blob/main/docs/usage/transactions.md

[S3] ZoneTree WAL modes: https://github.com/ZoneTree/ZoneTree/blob/main/docs/durability/wal-modes.md

[S4] ZoneTree production checklist: https://github.com/ZoneTree/ZoneTree/blob/main/docs/operations/production-checklist.md

[S5] ZoneTree iteration and range scans: https://github.com/ZoneTree/ZoneTree/blob/main/docs/usage/iteration-and-range-scans.md

[S6] Microsoft Learn, grain directory: https://learn.microsoft.com/en-us/dotnet/orleans/host/grain-directory

[S7] Microsoft Orleans project documentation, grain directories: https://dotnet.github.io/orleans/docs/host/grain-directory/

[S8] Microsoft Learn, Orleans transactions: https://learn.microsoft.com/en-us/dotnet/orleans/grains/transactions

[S9] ZoneTree.FullTextSearch: https://github.com/ZoneTree/ZoneTree.FullTextSearch

[S10] Apache Lucene.NET BM25Similarity: https://lucenenet.apache.org/docs/4.8.0-beta00017/api/core/Lucene.Net.Search.Similarities.BM25Similarity.html

[S11] USearch repository: https://github.com/unum-cloud/usearch

[S12] USearch C# SDK: https://unum-cloud.github.io/USearch/csharp/

[S13] .NEXT Raft: https://dotnet.github.io/dotNext/features/cluster/raft.html

[S14] .NEXT persistent WAL: https://dotnet.github.io/dotNext/features/cluster/wal.html

[S15] Elasticsearch reciprocal rank fusion: https://www.elastic.co/docs/reference/elasticsearch/rest-apis/reciprocal-rank-fusion

[S16] ZoneTree partitioning and replication: https://zonetree.dev/docs/building-systems/partitioning-and-replication/

[S17] ZoneTree transactional interface: https://github.com/ZoneTree/ZoneTree/blob/main/src/ZoneTree/ITransactionalZoneTree.cs

[S18] Orleans task scheduler: https://learn.microsoft.com/en-us/dotnet/orleans/implementation/scheduler

[S19] Orleans request scheduling: https://dotnet.github.io/orleans/docs/grains/request-scheduling/

[S20] Orleans stateless worker grains: https://learn.microsoft.com/en-us/dotnet/orleans/grains/stateless-worker-grains

[S21] SqlParser-cs: parser, dialects, syntax/semantics boundary: https://github.com/TylerBrinks/SqlParser-cs

[S22] Qdrant Query API, hybrid/multi-stage queries і RRF conventions: https://qdrant.tech/documentation/search/hybrid-queries/

[S23] Weaviate filtering, allowlists і ACORN: https://docs.weaviate.io/weaviate/concepts/filtering

[S24] Vespa phased ranking: https://docs.vespa.ai/en/ranking/phased-ranking.html

[S25] Qdrant multitenancy: https://qdrant.tech/documentation/manage-data/multitenancy/

[S26] Qdrant distributed deployment: https://qdrant.tech/documentation/scaling/distributed_deployment/

[S27] RavenDB RQL: https://docs.ravendb.net/7.2/querying/rql/what-is-rql

[S28] RavenDB vector search: https://www.ravendb.net/features/ai-features/vector-search

[S29] RavenDB time-series API: https://docs.ravendb.net/7.2/document-extensions/timeseries/client-api/overview

[S30] SurrealDB query language reference: https://surrealdb.com/docs/reference/query-language

[S31] SurrealDB search/fusion functions: https://surrealdb.com/docs/reference/query-language/functions/database-functions/search

[S32] ArangoDB vector functions: https://docs.arango.ai/arangodb/stable/aql/functions/vector/

[S33] ArangoDB graph traversals і pruning: https://docs.arango.ai/arangodb/stable/aql/graph-queries/traversals/

[S34] Neo4j Cypher SEARCH clause: https://neo4j.com/docs/cypher-manual/25/clauses/search/

[S35] PostgreSQL 18 row security: https://www.postgresql.org/docs/18/ddl-rowsecurity.html

[S36] PostgreSQL 18 full-text ranking: https://www.postgresql.org/docs/18/textsearch-controls.html

[S37] PostgreSQL 18 index types: https://www.postgresql.org/docs/18/indexes-types.html

[S38] pgvector filtering, multitenancy і iterative scans: https://github.com/pgvector/pgvector

[S39] PostgreSQL 18 WAL і synchronous commit: https://www.postgresql.org/docs/18/runtime-config-wal.html

[S40] SQL Server dynamic data masking та inference limitations: https://learn.microsoft.com/en-us/sql/relational-databases/security/dynamic-data-masking?view=sql-server-ver17

[S41] ParadeDB: hybrid search in PostgreSQL, 22 жовтня 2025: https://www.paradedb.com/blog/hybrid-search-in-postgresql-the-missing-manual

[S42] HNSW original paper, abstract: Malkov/Yashunin: https://arxiv.org/abs/1603.09320

[S43] ACORN paper, abstract: predicate-aware ANN: https://arxiv.org/abs/2403.04871

[S44] NaviX paper, abstract: graph-aware vector retrieval: https://arxiv.org/abs/2506.23397

[S45] Microsoft: .NET platform overview: https://dotnet.microsoft.com/en-us/learn/dotnet/what-is-dotnet

[S46] ZoneTree, Event Stores: https://github.com/ZoneTree/ZoneTree/blob/main/docs/building-systems/event-stores.md

[S47] ZoneTree, Queues: https://github.com/ZoneTree/ZoneTree/blob/main/docs/building-systems/queues.md

[S48] KurrentDB .NET client, Appending events: expected revision та atomic appends: https://docs.kurrent.io/clients/dotnet/v1.4/appending-events

[S49] KurrentDB persistent subscriptions, checkpoints і consumer groups: https://docs.kurrent.io/clients/node/v1.0/persistent-subscriptions

[S50] RabbitMQ, Consumer Acknowledgements and Publisher Confirms: https://www.rabbitmq.com/docs/confirms

[S51] RabbitMQ, Quorum Queues: https://www.rabbitmq.com/docs/quorum-queues

[S52] Microsoft Orleans, stream providers і durability memory streams: https://learn.microsoft.com/en-us/dotnet/orleans/streaming/stream-providers

[S53] Microsoft Orleans, messaging delivery guarantees: https://learn.microsoft.com/en-us/dotnet/orleans/implementation/messaging-delivery-guarantees

[S54] Marten tutorial, integration with Wolverine: https://martendb.io/tutorials/wolverine-integration

[S55] Wolverine, PostgreSQL durability і transport: https://wolverinefx.net/guide/durability/postgresql

[S56] PostgreSQL 18, SELECT і SKIP LOCKED для queue-like workloads: https://www.postgresql.org/docs/18/sql-select.html

### Межі перевірки джерел

Редакція 0.2 додає офіційні документації та вибіркове читання README через GitHub. Для ACORN, HNSW і NaviX використовувалися доступні abstracts; відтворення paper experiments і повний аудит implementation не виконувалися. Online docs можуть змінюватися. Publication/version labels не підміняють pinned package/source qualification.

У поточному читанні SqlParser-cs README мав blob SHA `2000a54f588a32428effd9fc464f75ce38b4adee`; pgvector README мав blob SHA `bcb7edaf91d2bb005675d378bb0ed495cc279c82`. Це ідентифікатори вмісту файлів, які не є repository commit IDs. Release pins і license checks залишаються у KL-001.


Редакція 0.3 використовує офіційні Event Store/queue patterns ZoneTree, KurrentDB client contracts, RabbitMQ delivery docs, Microsoft Orleans і PostgreSQL/.NET integration guides. Продуктивність, fault guarantees, native dependency matrix та interoperability не перевірялися запуском сторонніх систем. Приклади SDK/SQL є проєктними. Звичайне word-level твердження exactly-once у матеріалах frameworks не переноситься на довільні зовнішні effects; контракт KeyLoad визначений у 41.3.

Отримані через GitHub blob SHA: `docs/building-systems/event-stores.md` → `8eb7285c12f518a3f70cec3db04c72aff2cc5a6f`; `docs/building-systems/queues.md` → `1554aaab6e6408e366677f927b6a3db5dac5e284`. Це hashes окремих файлів; repository/package pins фіксуються в KL-001.
