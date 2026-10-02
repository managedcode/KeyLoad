# Storage maintainability repair brainstorm

The enabled numeric rules find a488-line provider file, a1051-line aggregate
ZoneTreeStore, an88-line checkpoint reader and three nesting violations. Merely
splitting partial files leaves the same oversized owner. Storage remains physically
node-local under PartitionHost; migrating grains receive non-owning references.

Recommended direction: retain the exact public facade and formats, and compose
real internal owners for per-store runtime/handles, initialization/identity,
journal recovery and publication, read view/transaction/ranges, checkpoint format
and generation replacement, and local BackupRestore. A single runtime contains
the existing lock, tree, journal, gate, identity, position and diagnostics session.
No second tree, cache, gate or recovery pass is allowed. Keep every real aggregate
type within200, executable unit50, file400 and nesting3; add no exception.

Rejected options: raise thresholds; move bodies into more facade partials; make
storage grains own handles; replace ZoneTree; add compatibility facades or public
test hooks. Those either evade the rule or change persistence/placement authority.

Parallel work is safe for a stateless strict checkpoint codec and a local backup
helper after their internal signatures freeze. Lead alone owns runtime/facade,
shared constants, existing partial conversion, generation replacement and all
docs/config/CI. Native benchmark readiness and PostgreSQL workers own separate
projects, with no shared writes. Test creation can use new disjoint real-store
files once a slot is free; no local qualification is allowed.

Risks: constructor-failure disposal is maintainer then journal then tree then
ownership, whereas normal disposal is maintainer then tree then journal then
ownership. Preserve both even when Dispose throws. The checkpoint decoder stops
at its footer during journal replay, while VerifySnapshot additionally requires
EOF. Install verifies incoming bytes before locking, verifies the staged copy
under the gate, and replays the swapped authoritative journal once. Compact and
replace-tree generation semantics differ. Do not hide these behind a new generic
cleanup/recovery path. Range observers, budget timing and logical counters must
retain the same work, including staged tombstones and lookahead.

This stage changes private responsibility boundaries only. Existing WAL/checkpoint/
identity/backup bytes, checksums, public errors, seeded process fault stages and
private modes remain exact. Larger resource repairs, retention/snapshot leases,
power-loss/endurance and matched server performance remain separate requirements.
Historic green CI is a baseline for the old SHA, not this decomposition.
