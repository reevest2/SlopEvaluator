# 5. Conventional Commits

Date: 2026-03-25
Status: Accepted

## Context

As the project grows and more contributors join, we need a consistent commit message format that supports automated changelog generation, semantic versioning, and clear project history. Without a standard, commit messages tend to vary widely in style and informativeness, making it difficult to understand what changed and why when reviewing history.

Options considered:
1. **No convention** -- Let contributors write free-form messages. Lowest friction but produces inconsistent, hard-to-parse history.
2. **Custom format** -- Define a project-specific convention. Requires documentation and has no tooling ecosystem.
3. **Conventional Commits** -- An industry-standard specification with broad tooling support for linting, changelog generation, and version bumping.

## Decision

We adopted the [Conventional Commits](https://www.conventionalcommits.org/) specification for all commit messages. The format is:

```
<type>(<optional scope>): <description>
```

Where `type` is one of: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `ci`, `chore`.

Scopes correspond to project names or cross-cutting concerns (e.g., `health`, `mutations`, `cli`, `orchestrator`).

## Consequences

- Commit history is consistent and machine-readable, enabling automated changelog generation from commit messages.
- The `type` prefix makes it easy to understand the nature of a change at a glance when scanning `git log`.
- Semantic versioning can be derived from commit types: `feat` triggers a minor bump, `fix` triggers a patch bump, and `BREAKING CHANGE` footers trigger a major bump.
- Contributors must learn the convention, but it is simple and well-documented.
- CI can optionally lint commit messages to enforce the format, catching mistakes before merge.
- The convention adds a small amount of overhead to each commit, but the long-term benefits to project history and automation outweigh this cost.
