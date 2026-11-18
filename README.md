# Dave's Discount Meats AddOn Manager

A Windows app for installing and updating WoW Forever addons from public GitHub Releases.

## For players

1. Run `DDMAddOnManager-Setup.exe` and confirm installation. The app is installed for your Windows account and appears in the Start menu.
2. Click **Browse** and select your WoW Forever game folder or its `Interface\AddOns` folder.
3. Select an addon with an available release and click **Install selected**. The manager asks before replacing an existing addon folder.
4. Restart WoW Forever after installing.

The manager compares the GitHub release tag with the installed addon's TOC version. Install or update while the game is closed. The app writes to the selected AddOns directory and leaves the game's separate SavedVariables directory alone.

Run a newer setup file to update the manager itself. To remove the manager, close it and use **Settings > Apps > Installed apps**. You can also run the manager directly from the extracted portable ZIP.

## Build and publish

Run `.\Build.ps1` with PowerShell 7 on 64-bit Windows. It builds `dist\DDMAddOnManager-Setup.exe`, the portable app ZIP, and uploadable addon ZIPs in `dist\release-assets`. Use `.\Build.ps1 -AppOnly` when building from a standalone manager checkout.

Commit this project as its own GitHub repository with `.github/workflows/release.yml` at the repository root. Run **Build Windows installer** manually in Actions to get a downloadable installer artifact. Push a tag such as `v0.1.0` to publish the versioned installer on the manager repository's Releases page. The build produces an unsigned installer.

For each addon release, set the TOC's `## Version:` to the release version, build the ZIP, and attach it to a public GitHub Release in that addon's catalog repository. Use a tag such as `v0.2.0` for TOC version `0.2.0`. The asset must be named `<AddonFolder>.zip` and contain `<AddonFolder>/<AddonFolder>.toc`. The manager reads the latest published stable release and downloads that named asset. [GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)

The catalog uses the existing Character Planner Export and DDM Action Bar Manager remotes. Fill in the `Repository` field for Forever API Explorer once it has a public repository and release.
