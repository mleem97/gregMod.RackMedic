# AGENTS.md — Notes for AI agents (gregMod.RackMedic)

Repo: https://github.com/mleem97/gregMod.RackMedic · License: Apache-2.0 · Version: see `VERSION` (1.0.0).

**Experimental mod** (`.experimental-mods/`, excluded from central
`build.sh` build/deploy). MelonMod for Data Center (`RackMedicMod`). Rack
diagnostics and repair: power, network, rendering, shop, workbench.

## Duties

1. **Read first:** `README.md`, `QUICKSTART.md`, `docs/INDEX.md` — only then make changes.
2. **Do not commit secrets** (keys, tokens, `.env`). Use keys only via environment variables.
3. **Preserve history:** no `push --force`, no history rewrite without instruction.
4. **Verify changes:** before reporting done, build and test whatever the repo
   provides (`QUICKSTART.md`, `scripts/`, `tests/` — `dotnet build gregMod.RackMedic.csproj -c Release`).
5. **Keep docs in sync:** for new features update `README.md` + `docs/` + `CHANGELOG.md` (Unreleased).
6. **Conventions:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:` …), one logical change per commit.
7. **When unsure:** stop and ask instead of guessing — especially for deletes, migrations, CI.

## Build and references

- Target: `net6.0`, x64. Game: Data Center.
- `references/` holds symlinks into the Steam install. Never commit
  `references/*.dll`, `bin/`, or `obj/`.
- Not covered by `ModRepositories/build.sh`; build directly in this folder.

## Hard rules

- Diagnostics are read-only; repairs go through the game's own methods so
  HUD and save stay consistent.
- **Never** touch gregCore types outside a soft-probe/JIT-split bridge.
- Security: `SECURITY.md` applies.

## Layout

- `src/RackMedicMod.cs` — MelonMod entry.
- `src/Core/`, `src/Patches/`, `src/Power/`, `src/Network/`,
  `src/Rendering/`, `src/Shop/`, `src/Workbench/` — feature areas.
- `scripts/`, `tests/`, `examples/`, `docs/` — tooling and docs.
