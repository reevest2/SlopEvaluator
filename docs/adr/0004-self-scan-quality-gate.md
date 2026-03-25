# 4. Self-Scan Quality Gate

Date: 2026-03-25
Status: Accepted

## Context

SlopEvaluator is a code quality tool. As such, it should demonstrate its own value by maintaining a high health score when evaluated by itself. This "eat your own dog food" approach serves two purposes: it validates that the tool produces meaningful results, and it keeps the SlopEvaluator codebase itself at a high quality standard.

We needed to decide whether to integrate self-scanning into the CI pipeline or treat it as an optional manual step.

## Decision

We run SlopEvaluator against its own codebase as a required CI quality gate in GitHub Actions. The `slop gate` command is executed with a minimum threshold, and the CI build fails if the score drops below that threshold. This check runs on every push and pull request.

## Consequences

- Every PR must maintain or improve the codebase health score, preventing gradual quality degradation.
- The self-scan acts as a live integration test of the tool itself -- if the scanner has a bug, it will likely surface during the CI run against the SlopEvaluator codebase.
- Contributors get immediate feedback if their changes reduce code quality below the threshold.
- The threshold needs to be set realistically and adjusted upward as the codebase matures, to avoid blocking legitimate contributions early on while still driving improvement.
- If the tool itself has a regression that causes false negatives (lower scores for healthy code), the CI gate could block unrelated PRs. This risk is mitigated by the test suite, which validates scorer behavior independently.
- The approach creates a virtuous cycle: improving the tool improves its ability to evaluate itself, which raises the bar for future contributions.
