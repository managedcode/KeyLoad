# ADR-141: збереження retirement authority в native snapshot та restore

Статус: обраний контракт реалізації. Компіляція, native recovery, Aspire RF3 та повний terminal DROP залишаються OPEN.

## Рішення

Фізичний owner зберігає незмінні lifetime fences та історію після retirement. Зменшення topology blockers не дозволяє старому snapshot повернути видалені дані чи втратити ці fences. Orleans RF3 acknowledgement, replication journal, atomic journal і node-local ZoneTree залишаються окремими обов'язковими механізмами. Цей контракт доповнює ADR-140; сам по собі він не кваліфікує terminal phase23.

Один канонічний native floor містить точний PhysicalShardRecord, revision, кількість завершених lifetime та SHA256 ланцюга. Нові обов'язкові поля floor: Id5 AuthorityRecordCount, Id6 AuthorityDigest і Id7 BaselineOriginKey. Незмінний step додає Id9 AddedAuthorityRecords та Id10 AddedAuthorityDigest; його SHA256 включає ці поля. Phase23 атомарно записує справжній owner release, step, floor і exactly-once marker перед зменшенням своїх guards. Повтор не збільшує floor і не зменшує counters знову. Відсутній committed outcome не означає повторне застосування ефекту.

Authority summary є кількістю та XOR32 окремих SHA256 від KeyCodec-encoded domain `keyload.resource-retirement.authority-record.v1`, точного ключа й точних native value bytes. Це комутативний integrity accumulator, а не SHA256 відсортованого потоку. Початковий accumulator: count0 та 32 нульові bytes. Native image відхиляє дублікати ключів. Неправильна кількість, змінені bytes чи пропущені authority records відхиляють цілий образ.

До mature set входять fence, source finalization history, owner cleanup та actual owner release лише коли існує точний release того самого lifetime/owner. Поточні counters, control, latest pointers і незавершені preparations не входять. Immutable source origins також входять. Сам exactly-once marker містить before/after floor, тому не включається в accumulator з циклічним self-hash. Окрема bounded перевірка зіставляє tokens `(exact marker key, step digest)` усіх перевірених steps і markers; marker повинен відтворити той самий native step з actual release та before/after authority delta. Відсутні, дубльовані чи змішані witnesses відхиляються.

## Поточний image format та upgrade contract

ZoneTree checkpoint format змінюється з 5 на 6. Існуючі metadata Id0..4 зберігаються; Id5 містить nullable floor, Id6 actual authority record count, Id7 actual authority digest. Null floor припустимий лише для доведеного never-issued стану без issued steps, markers, release records чи origins; digest завжди має точну довжину32. Типізовані floor/step/origin v1 є новими контрактами цієї стадії; відсутні обов'язкові authority fields не є підтримуваним старим варіантом.

Читач приймає тільки поточний format6. Format5, відсутній proof та незавершені metadata відхиляються до очищення дерева, закриття live owner чи публікації нового покоління. Автоматичної міграції, fallback, backfill, видалення checkpoint для прихованого replay або послабленого reader немає. Власник має зберігати вихідні дані й обирати лише окремо кваліфікований operational upgrade; цей source stage не обіцяє конвертер. Replication і atomic journals не видаляються та не замінюються image proof.

Writer використовує наявний повний native traversal. Reader перевіряє header/data/footer та bounded merge над тим самим відкритим файлом, з фіксованою кількістю cursor-ів і без map усієї історії чи другого storage engine. Всі проходи, lookup-и та recovery apply debt використовують один original record/byte/time budget. Повна вартість recovery перевіряється no-op apply до destructive replacement; limits не збільшуються для проходження тесту.

## Live replacement та окремі import/restore межі

Live replacement зберігає точний локальний floor. Рівний сумісний floor допустимий; новіший floor вимагає повного bounded step extension від захопленого локального floor. Старий, divergent чи missing proof відхиляється. Після всіх await/file робіт original live floor перечитується перед Replace. Позитивний incoming floor також має відповідати повному вже committed physical catalog, включно з physical id, incarnation, voters та placement epoch. Відсутній локальний catalog не є bootstrap shortcut.

Foreign atomic-partition import переносить лише закритий набір partition families. Глобальні target floor, fences, history та origins зберігаються; незалежні source/target floors не повинні бути рівними. Припущення про еквівалентність owner за однією incarnation недостатнє.

Fresh offline restore має окремі existing empty-target, нові identity/signer та joined staging gates. Джерело перевіряється під початковою identity і фактичним cut до rewrite. Точний перевірений mapping створює source origin; старі source authority rows залишаються незмінними. Target floor має власні revision0/count0, але успадковує validated source authority accumulator плюс новий origin. BaselineOriginKey вказує на один точний immutable origin; nested lineage обходиться лише цими links під original budget, depth і cycle checks. Прихованого reset source authority або registry scan немає.

Фактичний CLI використовує resumable native Slot restore. SourceOrigin зберігається в Reconciled commit Id6, переноситься через process cuts і застосовується один раз у тому самому AuthorityReset commit. До reset floor залишається source-owned, тоді як catalog уже має target mapping. Цей проміжний стан дозволений лише за точним typed retained SourceOrigin та admitted Slot context; звичайні image/live paths не отримують такого послаблення. Окремий origin row виникає тільки разом з target floor у StageFresh. Повне порівняння вихідного й restored image виключає лише попередньо перевірені змінені metadata keys.

## Qualification

Обов'язкові whole-operation gates: malformed/missing/current-format proofs; unchanged source/target та healthy continuation після відмови; recovery-budget debt до Replace; cold/replay; fresh та nested Slot lineage; genuine phase23 з actual owner×voter D4 settlement; stale snapshot, late-frame та exactly-once replay через справжній Aspire RF3 і SDK/MCP. Підготовлені fixtures, primitive encoding tests або source peer review не є виконаним RF3 чи power-loss доказом. Усі позитивні D3 blockers залишаються до доведеного еквівалентного guard та обов'язкової кваліфікації. Performance claims ця стадія не робить.
