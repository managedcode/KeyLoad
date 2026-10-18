# Native time-series durability and correction tags

In-process tests use the real ZoneTree owner and canonical atomic command apply.
Their complete receipts require `ProcessDurable` and exactly one persisted copy.
Public cluster tests independently require the fixture's configured persisted
copy count. Neither an enum nor an in-process receipt qualifies cluster recovery.

Tag selection uses an actual JSON string and ordinal equality. A numeric JSON
value does not match a string tag. Correction tests preserve independently
specified sample bits, sequences, revisions, generations, complete pages and
bounded refusal behavior; input and expected rows retain their original types.

Current authorization, cancellation, original-command replay and same-root cold
reopen remain mandatory. Full native execution and Linux qualification are open
until their original reports exist. No conversion or alternate matcher is added.
