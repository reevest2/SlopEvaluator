# SlopEvaluator — Claude Code Context

## Project Overview
.NET 10.0 CLI tool that combines codebase health scoring (14 dimensions) with mutation testing (10 Roslyn strategies). Scan → Improve → Verify loop for any .NET codebase.

## Build & Test
```bash
dotnet build
dotnet test
```

## Architecture
- `SlopEvaluator.Shared/` — Roslyn helpers, scoring interfaces, JSON defaults, process runner
- `SlopEvaluator.Health/` — 17 collectors measuring codebase health (from PromptEvaluator)
- `SlopEvaluator.Mutations/` — Mutation engine, 10 AST strategies, test runner (from MutationHarness)
- `SlopEvaluator.Orchestrator/` — Integration layer: scan→improve→verify pipeline
- `SlopEvaluator.Cli/` — CLI entry point with 8 commands
- `SlopEvaluator.Tests/` — Combined test suite

## Key Commands
```bash
slop scan <path>              # Full health scan
slop improve <path>           # Scan → mutate → suggest → rescan
slop gate <path> --threshold  # CI quality gate
```
