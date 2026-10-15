# Graphite RC1 verification / Проверки Graphite RC1

Version: **1.1.0-rc.1**. Date: **2026-10-16**. This is a test candidate; publication is a separate owner action.

## Recorded local result

- `./run-isolated-tests.ps1`: **573 passed** (App 91, CLI 32, Core 450), zero failures; Windows integration excluded.
- Debug/Release solution builds and the fixture host build: zero compiler warnings/errors.
- Four full fixture runs: RU/EN × dark/light, zero binding diagnostics; 216 generated PNGs including edge cases. Four focused Settings renders confirm its final compact layout. A fresh finishing review marked its five material fixes resolved; this covers those fixes, not unperformed Windows checks.
- Strict NuGet restore audit passed; the direct/transitive vulnerable-package query returned no known vulnerable packages.
- Portable ZIP allowlist: 29 files, **63,920,325 bytes**. Data and README entries match source, EXE ProductVersion is `1.1.0-rc.1`, and the checksum matches. No private-path or credential candidate was found in packaged text; the EXE was not executed by this check.
- ZIP SHA-256: `A670B2C53F4F36B49C5CC9336084D3CD02BCBF080A29A4DE9C38ACFAAEEEC5B5`.

## Scope and evidence

- Source review covered the tracked repository, new theme/resolver/recovery tests, data catalogs, docs, build scripts and fixture images. Two source security findings were retained in the private Codex Security workbench; follow-up patches were reviewed independently. The scan identity binds the starting source snapshot; it is not a certification of the final binary or a claim that every possible defect was found.
- Tests use guarded temporary roots and fake registry/command executors. `WindowsIntegration` tests are excluded. The visual host uses fixture view models and a plain WPF `Application`; it does not invoke production startup, real cleanup, services, registry changes or automatic cleanup.
- Both themes and RU/EN are rendered at 1120×700 and 1360×860, at 1× and 2×. The host checks bindings, visible rows, overlays, navigation, persistence, live theme changes and fake first-run behavior. A 2× render does **not** prove behavior after changing Windows DPI.
- WPF-UI caption foreground initialization runs on the caption button's `Loaded` event. The headless host raises only that dependency event; it does not show the production application or execute application startup.
- A fresh visual review checks the actual screenshots against the user's approved dark/light Graphite boards. The Impeccable engine was unavailable: no engine seed, measured comp specification, comp-diff score, quality-card result or phase closure is claimed or fabricated. The implementation follows native WPF constraints and the approved product flow.
- NuGet audit checks direct and transitive dependencies; GitHub Actions references are pinned to the verified official action commit revisions. The portable package excludes source, PDBs, profile state, quarantine, logs, recovery records and internal development notes.

## Intentional compatibility boundaries

Approved Windows utility aliases resolve to trusted absolute Windows executable paths. Arbitrary/qualified commands from custom catalogs are rejected rather than searched through the current directory or PATH.

Quarantine restoration rejects reparse traversal and renames verified objects relative to a retained destination directory, without replacing existing objects. Missing parents, legacy directory entries, collisions and trailing directory separators are tested. Legacy cross-volume restoration remains blocked with its source/journal preserved for manual recovery. Do not delete such a session.

## Outstanding release gates

Real Windows build compatibility, actual Windows DPI changes, assistive technology, real system operations, a second fixture volume and five novice-user trials remain outstanding. Tests of real Windows files/settings require a separately approved disposable environment. The EXE is unsigned. The project's distribution/open-source license remains the owner's decision.

Do not label this build stable or promise universal safety/performance. Follow [the stable release checklist](STABLE_RELEASE_CHECKLIST.md) before a stable release.
