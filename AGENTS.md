# Cabbie: agent instructions

Guidance for coding agents (omp, Cursor, Claude Code, OpenCode) and humans working in this repo. Detailed rules live in `.cursor/rules/*.mdc`; they apply to every agent, not only Cursor. Read them before changing code.

| Rule | What it covers |
| --- | --- |
| `.cursor/rules/clean-architecture.mdc` | Onion layers, ports-only Application, adapters stay out of core |
| `.cursor/rules/testing.mdc` | Behaviour tests required for every slice; no test-first ritual |
| `.cursor/rules/graphite-workflow.mdc` | Graphite stacks, one intent per draft PR, conventional commits |

## Workflow

1. **Plan top-down.** Start from the README roadmap item. Write a short plan that names the slice, its **Prove with** behaviours, and the 1–N PRs it splits into, in dependency order. Keep each PR to one intent.
2. **Build with Graphite.** One branch per intent: `gt create -m "type: lowercase description" type/brief-name`. Stack only when a PR depends on the one below; otherwise branch off `main`. Submit with `gt submit --stack --draft`. Never merge; humans merge.
3. **Test behaviour.** Every slice ships with tests that pin behaviour, not implementation. Write them alongside or after the code. See `testing.mdc`.
4. **Check before handing off** (all must pass):

   ```bash
   dotnet build Cabbie.slnx                     # analyzers run in the build; warnings are errors
   dotnet test Cabbie.slnx
   dotnet test tests/Cabbie.Tests.Postgresql    # needs Docker (Testcontainers)
   dotnet format Cabbie.slnx --verify-no-changes --no-restore --severity warn --include <changed .cs files>
   ```

   Fix formatting with the same `dotnet format` command minus `--verify-no-changes`.

## Analyzers (ratchet)

`Directory.Build.props` enables `AnalysisMode=Recommended` with `TreatWarningsAsErrors`. Rules that already had violations are listed as `suggestion` in `.editorconfig` (section "Roslyn analyzer baseline"). Every other rule is a build error.

- Fixing a rule's last violation means deleting its baseline line.
- Do not add baseline lines, `#pragma` or `NoWarn` for new code. Fix the violation. A `[SuppressMessage(..., Justification = "...")]` is for the rare case where a fix would break a public contract.

## Commits and PRs

- Conventional commits: `<type>: <lowercase description>`. Types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`. The PR title must be conventional too; CI checks it.
- Fill in `.github/pull_request_template.md`: summary, stack position, behaviour covered, checks run.
- Linear history. No merge commits on feature branches; restack with `gt restack` / `gt modify`.

## Do not

- Reference an adapter project (`Cabbie.Postgresql`, `.RabbitMQ`, `.Http`, future `.Aws`) from `src/Cabbie`.
- Skip, delete or weaken a test to get green.
- Commit secrets, connection strings with passwords, or machine-specific paths.
