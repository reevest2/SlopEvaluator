# Contributing to SlopEvaluator

Thank you for your interest in contributing to SlopEvaluator. This guide explains the process for contributing to this project.

## Getting Started

### Fork and Clone

1. Fork the repository on GitHub.
2. Clone your fork locally:
   ```bash
   git clone https://github.com/<your-username>/SlopEvaluator.git
   cd SlopEvaluator
   ```
3. Add the upstream remote:
   ```bash
   git remote add upstream https://github.com/your-org/SlopEvaluator.git
   ```

### Setting Up the Dev Environment

1. Install the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Restore dependencies:
   ```bash
   dotnet restore
   ```
3. Build the solution:
   ```bash
   dotnet build
   ```
4. Run the tests to confirm everything passes:
   ```bash
   dotnet test
   ```

## Coding Standards

### Conventional Commits

This project uses [Conventional Commits](https://www.conventionalcommits.org/). Every commit message must follow this format:

```
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```

**Types:**
- `feat` -- a new feature
- `fix` -- a bug fix
- `docs` -- documentation only changes
- `style` -- formatting, missing semicolons, etc. (no code change)
- `refactor` -- code change that neither fixes a bug nor adds a feature
- `perf` -- performance improvement
- `test` -- adding or correcting tests
- `ci` -- CI configuration changes
- `chore` -- maintenance tasks

**Examples:**
```
feat(health): add cyclomatic complexity collector
fix(mutations): handle empty syntax trees gracefully
docs: update README with new CLI commands
```

### Code Style

- Follow the `.editorconfig` settings in the repository root.
- Use file-scoped namespaces.
- Use `var` when the type is apparent.
- Prefer braces for multi-line blocks.
- Sort `System` usings first.

### Testing

- All new features must include unit tests.
- All bug fixes should include a regression test.
- Tests live in `SlopEvaluator.Tests/`.

## Submitting Pull Requests

1. Create a feature branch from `master`:
   ```bash
   git checkout -b feat/my-feature master
   ```
2. Make your changes in small, focused commits using conventional commit messages.
3. Ensure all tests pass:
   ```bash
   dotnet test
   ```
4. Push your branch to your fork:
   ```bash
   git push origin feat/my-feature
   ```
5. Open a pull request against the `master` branch of the upstream repository.
6. Fill in the PR template with a description of your changes, the motivation, and any relevant context.

## Code Review Process

- All PRs require at least one approving review before merge.
- CI must pass (build, tests, and the self-scan quality gate).
- Reviewers may request changes -- please address feedback in additional commits rather than force-pushing, so the review history is preserved.
- Once approved and CI is green, a maintainer will merge the PR using squash-and-merge.

## Reporting Issues

- Use GitHub Issues to report bugs or request features.
- Include steps to reproduce for bug reports.
- Include the .NET SDK version and OS in bug reports.

## Questions

If you have questions about contributing, open a Discussion on GitHub or reach out to the maintainers.
