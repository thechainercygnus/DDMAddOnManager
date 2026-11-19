# Dave's Discount Meats AddOn Manager

Part of a set of [WoW: Forever tools](https://github.com/thechainercygnus).

A Windows app for installing and updating World of Warcraft: Forever add-ons from public GitHub releases.

[Download the latest Windows installer](https://github.com/thechainercygnus/DDMAddOnManager/releases/latest)

## Screenshots

Dark and Light modes with two add-ons selected. The folder and versions shown are examples.

![Dark mode with two add-ons selected and the Install 2 add-ons button visible](docs/manager-dark.png)

![Light mode with the same add-ons selected](docs/manager-light.png)

## Install or update the manager

1. Open the latest release and download **DDMAddOnManager-Setup.exe** from **Assets**. The installer is unsigned, so Windows may display a SmartScreen warning.
2. Run Setup. It installs the manager for your Windows account and adds it to the Start menu.
3. To upgrade, close the manager and run the newer installer. Setup replaces the existing copy in place.
4. To remove the manager, close it and go to **Settings > Apps > Installed apps**.

If you have a portable ZIP from a local build, extract it and run the manager directly.

## Install and update add-ons

1. Launch the manager. It checks Windows installation records and common folders on local drives. If it finds one WoW Forever installation, it fills in the **WoW Forever AddOns folder** automatically.
2. If the field is empty or points to the wrong copy, click **Browse…**. Choose the World of Warcraft root, its `_forever_` client folder, or the client's `Interface\AddOns` folder. The manager remembers a manually selected folder while it exists.
3. Click each tile you want to install or update. A checkmark indicates that the tile is selected. Click a selected tile again to deselect it.
4. Click **Install N add-ons**. If any selected add-ons will be replaced, the manager shows one confirmation listing them. The install count includes only selected add-ons with downloadable packages.
5. Restart WoW Forever to load the changes.

The manager compares the installed add-on's TOC version with the latest GitHub release tag. **Current** means the versions match. **Version differs** means installing will replace the local copy with the GitHub release. The details beneath the tiles show both versions for the most recently clicked tile. Click **Refresh Available** to check GitHub again.

Install or update while the game is closed. The manager writes into the selected AddOns directory. WoW's SavedVariables are stored separately.

Choose **System**, **Light**, or **Dark** in the header. System follows the Windows app color setting, and the manager remembers your choice.

## Build and publish

Use PowerShell 7 on 64-bit Windows.

- Run `.\Build.ps1 -AppOnly` from a standalone checkout to build `dist\DDMAddOnManager-Setup.exe` and the portable ZIP.
- Run `.\Build.ps1` from the manager checkout alongside the add-on projects to also package add-on ZIPs under `dist\release-assets`.
- Run **Build Windows installer** manually from the GitHub Actions tab to upload an installer artifact. Push a stable tag such as `v0.1.0` to publish the versioned installer under GitHub Releases.

## Add-on catalog and release packages

The catalog in [addons.json](addons.json) lists Character Planner Export, DDM Action Bar Manager, and Forever API Explorer. Each entry specifies the add-on folder, display name, and GitHub repository.

For each add-on release, set the TOC's `## Version:` to the release version and publish a stable GitHub release with a matching tag, such as `v0.2.0` for TOC version `0.2.0`. Attach a ZIP named `<AddonFolder>.zip` containing `<AddonFolder>/<AddonFolder>.toc`. The manager checks the latest public stable release and downloads that exact asset.

## License

[MIT](LICENSE). World of Warcraft names and game data belong to Blizzard
Entertainment. This project isn't affiliated with or endorsed by Blizzard.
