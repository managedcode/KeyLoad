# Bounded storage metadata brainstorm

Goal: remove unbounded metadata allocations and the duplicate identity-file read
from real local backup restore without changing WAL, durability or physical owner.

Current `backup.json` and `identity.json` readers use File.ReadAllBytes. Restore
first streams identity for the manifest hash, verifies WAL, then reads identity
again for its envelope. A whitespace-padded file can cause a large allocation;
the second identity read need not observe the bytes whose outer hash was checked.
The current strict JSON options already reject unknown fields and missing/null
required members. System.Text.Json byte[] is base64, not a JSON integer array;
the manifest is below512 compact bytes and a valid identity envelope below1024.

Options: retain unbounded allocation (reject); stream JSON independently of hash
(still risks observing different bytes and retains unnecessary parsing machinery);
or read one finite owned byte region and verify/parse that same region (chosen).

Choose an inclusive16KiB manifest limit and4KiB identity-envelope limit, named
once in the private persistence-format owner. These exceed normal compact output
and allow whitespace within the limit. Over-limit input, including formerly
accepted excessive whitespace, is now unsupported metadata, with the existing
typed FormatUnsupported/detail for that file. Persisted schema/version stays exact.

Preserve destination-conflict-first ordering, manifest structure validation,
manifest file-order verification and all ordinary checksum/JSON/I/O failures.
Capture verified identity bytes while iterating manifest files; parse them only
after every outer file verification, so corrupt WAL still precedes malformed
identity JSON as before. Startup keeps owner-lock acquisition before identity
read and releases all partially acquired resources on failure.

Use real public store creation, backup, restore/reopen, actual filesystem edits,
independent SHA computation and measurable allocation assertions in GitHub TUnit.
No timing-race growth test, fake stream, reader hook, local test or physical-I/O
claim. Static review must prove opened-file length precheck plus at most limit+1
authoritative bytes and hashing/parsing of the same owned identity region.

Risks: restricting extreme padding; changing ordinary error precedence; exposing
keys in evidence; accidental journal cap or weakened flush barrier. Keep these
explicit in acceptance/ADR. No new dependency or ManagedCode defect is involved.
No open architecture question blocks this bounded private implementation; full
RF3/power-loss/endurance and numeric coverage qualification remain separate.
