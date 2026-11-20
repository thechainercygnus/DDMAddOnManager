# CLAUDE.md

Instructions for AI coding agents working in this repo.

## What this is

"Dave's Discount Meats AddOn Manager" (GitHub: `thechainercygnus/DDMAddOnManager`): a
Windows Forms app in C# that installs and updates WoW: Forever addons from public GitHub
releases. `AddonManager.cs` is the app, `AddonTile.cs` the tile control, `Setup.cs` the
per-user installer, `addons.json` the bundled catalog (embedded as a resource).

## Commands

PowerShell 7 on 64-bit Windows. The build uses the .NET Framework 4 `csc.exe` from
`%WINDIR%`; no SDK, NuGet or project file.

```
.\Build.ps1 -AppOnly
```

builds `dist\DDMAddOnManager-Setup.exe` and the portable ZIP. `.\Build.ps1` (no switch)
also zips each catalog addon from its sibling folder (`..\<Folder>\<Folder>`) into
`dist\release-assets`. There are no automated tests; build, then run the app to check.

Release: the user tags `vX.Y.Z`; `release.yml` builds with that version and publishes the
installer. Tagging is the user's call.

## Rules

- C# 5 / .NET Framework 4 only (the in-box compiler): no newer language features or
  packages. New source files must be added to both `csc` calls in `Build.ps1`.
- The manager writes into the user's game folder: validate archive paths (no `..`,
  absolute paths or folders outside the addon), confirm before replacing an addon, and
  never touch `WTF`/SavedVariables.
- Only download from `https://github.com` / GitHub API release assets, over HTTPS.
- Release contract: ZIP named `<Folder>.zip` containing `<Folder>/<Folder>.toc`, whose
  `## Version:` matches the tag. Keep it in step with the addons' `release.yml`.
- Keep `addons.json` in step with the addon repos and the README's catalog list.
- Don't commit `dist/` (gitignored). Update `docs/` screenshots after visible UI changes.
- CI: `secret-scan`, `codeql` and `dependency-review` must pass. Pin Actions to a full
  commit SHA with the version as a comment; give each job only the permissions it needs.
- Commits and pushes need the user's go-ahead. Work on a branch.
