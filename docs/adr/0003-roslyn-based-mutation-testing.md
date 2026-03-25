# 3. Roslyn-Based Mutation Testing

Date: 2026-03-25
Status: Accepted

## Context

Mutation testing validates test suite effectiveness by introducing small code changes (mutants) and checking whether tests detect them. We needed to choose the level at which mutations are applied:

1. **IL-level mutation** -- Modify compiled bytecode directly. Tools like Stryker.NET use this approach. It is fast because it avoids recompilation, but mutants can be difficult to map back to source code, and IL manipulation is fragile across .NET runtime versions.
2. **Source-level (text) mutation** -- Apply string-level find-and-replace on source files. Simple but imprecise; can produce syntactically invalid code or miss semantic nuances.
3. **AST-level mutation via Roslyn** -- Parse source into a Roslyn syntax tree, apply structured transformations, and emit modified source. Mutants are always syntactically valid, easy to map to source locations, and the approach benefits from Roslyn's mature API.

## Decision

We chose Roslyn for AST-level mutation generation. The mutation engine parses each source file into a `SyntaxTree`, applies one of 10 mutation strategies (arithmetic operator replacement, boolean literal inversion, conditional boundary changes, return value mutation, etc.), and writes the modified source back for test execution.

## Consequences

- All generated mutants are syntactically valid C#, eliminating wasted time on mutations that fail to compile.
- Mutants map directly to source locations (file, line, column), making results easy to understand and act on.
- Roslyn's `SyntaxRewriter` pattern makes it straightforward to implement new mutation strategies by subclassing and overriding visitor methods.
- The approach requires recompilation after each mutation, which is slower than IL-level mutation. This trade-off is acceptable because correctness and debuggability are prioritized over raw speed.
- The engine is tied to C# and the Roslyn compiler platform, so it does not support other .NET languages (F#, VB.NET) without additional work.
- Roslyn is a large dependency, but it is already required for health scoring (complexity analysis, syntax inspection), so the incremental cost is zero.
