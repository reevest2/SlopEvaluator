# SlopEvaluator — Claude Code Context

## Project Overview
.NET 10.0 CLI tool that combines codebase health scoring (14 dimensions) with mutation testing (16 strategies: 12 AST walker + 4 structural). Scan → Improve → Verify loop for any .NET codebase.

## Build & Test
```bash
dotnet build
dotnet test
```

## Architecture
- `SlopEvaluator.Shared/` — Roslyn helpers, scoring interfaces, JSON defaults, process runner
- `SlopEvaluator.Health/` — 17 collectors measuring codebase health (from PromptEvaluator)
- `SlopEvaluator.Mutations/` — Mutation engine, 16 strategies (12 AST + 4 structural), test runner
- `SlopEvaluator.Orchestrator/` — Integration layer: scan→improve→verify→report pipeline
- `SlopEvaluator.Cli/` — CLI entry point with 9 commands
- `SlopEvaluator.Tests/` — Combined test suite

## Key Commands
```bash
slop scan <path>              # Full health scan
slop report <path>            # Diagnostic report with recommendations
slop improve <path>           # Scan → mutate → suggest → rescan
slop mutate <file> --roslyn   # Run mutation testing on a file
slop gate <path> --threshold  # CI quality gate
```
