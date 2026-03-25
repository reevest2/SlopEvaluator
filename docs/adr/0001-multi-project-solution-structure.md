# 1. Multi-Project Solution Structure

Date: 2026-03-25
Status: Accepted

## Context

SlopEvaluator combines two distinct capabilities: codebase health scoring and mutation testing. These features share some infrastructure (Roslyn helpers, process execution) but are otherwise independent. We needed to decide how to organize the codebase -- as a single monolithic project, a two-project split, or a more granular layered structure.

A monolithic project would be simpler initially but would couple unrelated concerns, making it harder to test components in isolation and increasing build times during development. A two-project split (health + mutations) would miss the opportunity to separate the shared infrastructure, CLI surface, and orchestration logic.

## Decision

We adopted a 6-project layered solution structure:

- **SlopEvaluator.Shared** -- Common infrastructure: Roslyn helpers, scoring interfaces, JSON serialization defaults, and process runner utilities.
- **SlopEvaluator.Health** -- 17 collectors that measure codebase health across 14 dimensions. Depends only on Shared.
- **SlopEvaluator.Mutations** -- Mutation engine with 10 AST-level strategies and a test runner. Depends only on Shared.
- **SlopEvaluator.Orchestrator** -- Integration layer that composes Health and Mutations into the scan, improve, and verify pipeline. Depends on Shared, Health, and Mutations.
- **SlopEvaluator.Cli** -- Thin CLI entry point exposing 8 commands. Depends on Orchestrator.
- **SlopEvaluator.Tests** -- Combined test suite covering all projects.

Dependencies flow in one direction: Cli -> Orchestrator -> Health/Mutations -> Shared.

## Consequences

- Each layer can be built and tested independently, enabling faster feedback during development.
- The unidirectional dependency graph prevents circular references and keeps coupling low.
- Health and Mutations have no dependency on each other, so changes in one do not affect the other.
- New features (e.g., additional scoring dimensions or mutation strategies) can be added to the appropriate project without touching unrelated code.
- The 6-project structure adds some overhead in terms of project files and cross-project references, but this is minimal with the slnx format.
- Contributors need to understand which project a change belongs in, but the naming convention makes this straightforward.
