# 2. Weighted Health Scoring

Date: 2026-03-25
Status: Accepted

## Context

SlopEvaluator measures codebase health across 14 dimensions (e.g., complexity, documentation coverage, test quality, naming conventions). We needed a strategy for aggregating these individual dimension scores into a single overall health score that could be used for CI quality gates and trend tracking.

Options considered:
1. **Simple average** -- Treat all dimensions equally. Easy to understand but does not reflect the reality that some dimensions matter more than others.
2. **Minimum score** -- Use the lowest dimension as the overall score. Too punitive; a single weak area would dominate regardless of overall quality.
3. **Weighted average** -- Assign configurable weights to each dimension. More nuanced and allows teams to emphasize what matters most to them.

## Decision

We use weighted averaging across all 14 dimensions. Each dimension has a default weight, and the overall score is computed as the sum of (score * weight) divided by the sum of weights. Weights are configurable so that teams can adjust the emphasis to match their priorities (e.g., a team focused on reliability might increase the weight of test coverage and decrease the weight of naming conventions).

## Consequences

- The default weights provide a sensible out-of-the-box experience for most projects.
- Teams can customize weights to match their specific quality priorities without modifying code.
- The weighted average is more representative of overall health than a simple average or minimum.
- Adding a new dimension requires choosing an appropriate default weight, which is a subjective decision that should be documented.
- The quality gate threshold (`slop gate --threshold`) operates on the weighted aggregate, so changing weights can affect whether a project passes or fails the gate.
- Weight configuration needs to be documented clearly so users understand how the overall score is derived.
