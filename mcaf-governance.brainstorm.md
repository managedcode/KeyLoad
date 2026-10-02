# MCAF governance bootstrap

The repository needs the canonical MCAF workflow without losing its existing product, dependency ownership, RF3, Orleans, TUnit, Docker, security, or delivery rules. The user explicitly prohibits skill installation for this task.

## Options and decision

1. Replace AGENTS.md with the template: rejected because this deletes customized policy.
2. Install a complete skill bundle: rejected by the user's explicit instruction.
3. Append customized current template requirements, create project-local governance and a repository architecture map, and verify preservation: chosen.

The existing root is the preservation baseline. Installation changes governance and documentation; it does not claim that existing xUnit, DotNext, host-process tests or missing MCP surfaces already satisfy the newer policy.

## Ownership and risks

- Root governance, shared documentation, the installation record and integration have one owner: the lead.
- Project-local files and a static governance validator are separate bounded coding tasks after the feature specification and ADR exist.
- Read-only architectural review uses the highest-capability suitable model.
- Existing dirty production changes belong to other work and must survive.
- Template exceptions and deletion of obsolete policy must not weaken an existing rule; conflicting guidance is recorded explicitly.
- All qualification results and public benchmark numbers come from GitHub Actions. Local inspection and builds are development checks.

## Validation

Hash the exact pre-installation root prefix, inventory every csproj and module, validate local policy coverage and documentation links, inspect the entire diff, join all workers, and verify the integrated state. Use the successful GitHub Actions run for commit 9c570f8c33a7a9667507a8e1c0ca68860de3be45 as the recorded baseline. No local load test or invented performance result is eligible for publication.
