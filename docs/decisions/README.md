# Decisions

Architecture Decision Records. Each file captures one decision —
context, the choice made, and consequences — at the point it was made.

Files are immutable once `Accepted`. To change a decision, write a new
ADR that explicitly supersedes the old one (`Supersedes: 0007`) and
flip the old one's status to `Superseded by 0014`.

Status values:

- **Proposed.** Drafted, not yet agreed.
- **Accepted.** Current rule. Agents follow Accepted ADRs.
- **Superseded by NNNN.** Historical. Do not act on it; read the
  superseding ADR.
- **Rejected.** Considered, not adopted. Kept so the next person
  doesn't re-propose the same thing without knowing why it lost.
- **Deprecated.** No longer applies, no superseding ADR needed
  (system removed entirely).

Filename: `NNNN-kebab-case-summary.md`. Numbers are zero-padded to 4.
Use the next free number — don't reuse numbers from rejected ADRs.

Template: [`0000-template.md`](0000-template.md).
