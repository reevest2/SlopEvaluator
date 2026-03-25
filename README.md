# SlopEvaluator

[![CI](https://github.com/your-org/SlopEvaluator/actions/workflows/ci.yml/badge.svg)](https://github.com/your-org/SlopEvaluator/actions/workflows/ci.yml)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

A .NET 10.0 CLI tool that combines **codebase health scoring** (14 dimensions) with **mutation testing** (10 Roslyn-based strategies). Run SlopEvaluator against any .NET codebase to **Scan, Improve, and Verify** code quality in a continuous loop.

## Installation

### From Source

```bash
git clone https://github.com/your-org/SlopEvaluator.git
cd SlopEvaluator
dotnet build
```

### As a .NET Tool

```bash
dotnet tool install --global SlopEvaluator.Cli
```

### Docker

```bash
docker build -t slopevaluator .
docker run --rm -v /path/to/your/project:/workspace slopevaluator scan /workspace
```

## Setup

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- Git (for version control integration)

### Development Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/your-org/SlopEvaluator.git
   cd SlopEvaluator
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the solution:
   ```bash
   dotnet build
   ```

4. Run the tests to verify everything works:
   ```bash
   dotnet test
   ```

## Usage

SlopEvaluator provides 8 CLI commands for codebase analysis and improvement.

### Scan a Codebase

Run a full 14-dimension health scan against a target project:

```bash
slop scan <path-to-project>
```

### Improve Code Quality

Run the full Scan, Improve, Verify pipeline -- scans the codebase, generates mutations, suggests improvements, and rescans to measure impact:

```bash
slop improve <path-to-project>
```

### CI Quality Gate

Use as a CI quality gate that fails the build if the health score falls below a threshold:

```bash
slop gate <path-to-project> --threshold 70
```

### Mutation Testing

Run mutation testing in isolation:

```bash
slop mutate <path-to-project>
```

### Additional Commands

```bash
slop quality <path>     # Show quality summary
slop compare <a> <b>    # Compare two scan results
slop history <path>     # Show scan history over time
slop fix <path>         # Apply suggested fixes
```

## Architecture

SlopEvaluator follows a layered 6-project solution structure:

```
SlopEvaluator.slnx
  |-- SlopEvaluator.Shared        # Roslyn helpers, scoring interfaces, JSON defaults, process runner
  |-- SlopEvaluator.Health         # 17 collectors measuring codebase health across 14 dimensions
  |-- SlopEvaluator.Mutations      # Mutation engine with 10 AST-level strategies and test runner
  |-- SlopEvaluator.Orchestrator   # Integration layer: scan -> improve -> verify pipeline
  |-- SlopEvaluator.Cli            # CLI entry point with 8 commands
  |-- SlopEvaluator.Tests          # Combined test suite
```

### Health Scoring

The health scanner evaluates code across 14 weighted dimensions including complexity, documentation coverage, test quality, naming conventions, and more. Each dimension produces a 0-100 score, and a weighted aggregate produces the overall health score.

### Mutation Testing

The mutation engine uses Roslyn to perform AST-level code transformations across 10 strategies (arithmetic operator replacement, boolean literal inversion, conditional boundary changes, and more). Surviving mutants indicate areas where test coverage is weak.

## Build

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish self-contained
dotnet publish SlopEvaluator.Cli -c Release -o ./publish
```

## Testing

Run the full test suite:

```bash
dotnet test
```

Run with verbose output:

```bash
dotnet test --verbosity normal
```

Run with coverage:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Contributing

Contributions are welcome. Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to get started.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
