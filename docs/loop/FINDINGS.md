# FINDINGS — one line per finding (D4 SWEEP). Append-only; the loop's working memory of what is wrong or wanting on main.

Line format (pipes; one line; ≤ 600 characters):
`F-NNN | date | class | severity | where | what | evidence | proposed action | cost | disposition`
- class: bug / polish / visual / perf / inconsistency / best-practice / feature / purchase / readiness
- severity: S0 red main or crash · S1 player-visible defect · S2 quality or drift · S3 nit
- where: a path and line, a scene and object, a doc section
- evidence: a pointer, never a claim — path:line, a console line, a screenshot under .utmp/factory/sweeps/, a harness row
- cost: S / M / L
- disposition: AUTO→CHG-NNN · ASK→NEEDS-GREY · SPIKE→SPIKES L<n> · NOTE · DROP (reason)

A sweep that re-reports a finding already here is a defect in the sweep: grep before appending.

(empty)
