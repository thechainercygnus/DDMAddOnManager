using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle(Manager.ProductTitle)]
[assembly: AssemblyProduct(Manager.ProductTitle)]

internal sealed class Addon
{
    public string Folder { get; set; }
    public string Name { get; set; }
    public string Repository { get; set; }
    internal bool IsQuestieBundle
    {
        get { return String.Equals(Repository, "Questie/Questie", StringComparison.OrdinalIgnoreCase); }
    }

    internal bool IsQuestieDatabase
    {
        get { return String.Equals(Repository, "Questie/QuestieDB", StringComparison.OrdinalIgnoreCase); }
    }

    internal string[] Folders
    {
        get { return IsQuestieBundle ? new[] { "Questie", "QuestieDB" } :
            new[] { IsQuestieDatabase ? "QuestieDB" : Folder }; }
    }

    internal string Manifest(string folder)
    {
        return folder + (IsQuestieBundle || IsQuestieDatabase ? "_Forever.toc" : ".toc");
    }

    internal string[] ReleaseVersions(string tag)
    {
        if (!IsQuestieBundle)
            return new[] { tag };

        Match bundle = Regex.Match(tag ?? "", @"\Abundle/v([0-9]+\.[0-9]+\.[0-9]+)\+v([0-9]+\.[0-9]+\.[0-9]+)\z");
        return bundle.Success ? new[] { bundle.Groups[1].Value, bundle.Groups[2].Value } : null;
    }

    internal Release Latest;
    internal bool Custom;
    internal AddonTile Tile;
}

internal sealed class Release
{
    public string tag_name { get; set; }
    public Asset[] assets { get; set; }
}

internal sealed class Asset
{
    public string name { get; set; }
    public string browser_download_url { get; set; }
}

internal static class AddonFiles
{
    internal static string VersionAt(string directory, string manifestName)
    {
        string manifest = Path.Combine(directory, manifestName);
        if (!File.Exists(manifest))
            return null;
        string version = File.ReadLines(manifest).FirstOrDefault(line =>
            line.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase));
        return version == null ? null : version.Substring("## Version:".Length).Trim();
    }

    internal static bool SameVersion(string left, string right)
    {
        return !String.IsNullOrWhiteSpace(left) && !String.IsNullOrWhiteSpace(right)
            && String.Equals(left.TrimStart('v', 'V'), right.TrimStart('v', 'V'),
                             StringComparison.OrdinalIgnoreCase);
    }

    internal static void Install(string directory, Addon addon, string archive, string tag)
    {
        string[] folders = addon.Folders;
        string[] versions = addon.ReleaseVersions(tag);
        if (versions == null)
            throw new InvalidDataException("Unrecognized package version.");

        string staging = Path.Combine(directory, ".addon-manager-" + Guid.NewGuid().ToString("N"));
        string extracted = Path.Combine(staging, "package");
        string backup = Path.Combine(staging, "previous");
        Directory.CreateDirectory(backup);
        var backedUp = new List<string>();
        var installed = new List<string>();
        bool preserveBackup = false;
        try
        {
            ZipFile.ExtractToDirectory(archive, extracted);
            for (int index = 0; index < folders.Length; index++)
            {
                string folder = folders[index];
                string manifest = addon.Manifest(folder);
                string destination = Path.Combine(directory, folder);
                if (Directory.Exists(destination) &&
                    (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The installed " + folder + " folder is a link.");

                string source = Path.Combine(extracted, folder);
                if (!SameVersion(VersionAt(source, manifest), versions[index]))
                    throw new InvalidDataException("The ZIP must contain " + folder + "/" + manifest +
                        " with a version matching " + versions[index] + ".");

                // Older Forever clients look for the Camelot filename.
                if (addon.IsQuestieBundle || addon.IsQuestieDatabase)
                {
                    string alias = Path.Combine(source, folder + "_Camelot.toc");
                    if (!File.Exists(alias))
                        File.Copy(Path.Combine(source, manifest), alias);
                }
            }

            foreach (string folder in folders)
            {
                string destination = Path.Combine(directory, folder);
                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, Path.Combine(backup, folder));
                    backedUp.Add(folder);
                }
                Directory.Move(Path.Combine(extracted, folder), destination);
                installed.Add(folder);
            }
        }
        catch
        {
            try
            {
                foreach (string folder in Enumerable.Reverse(installed))
                    Directory.Move(Path.Combine(directory, folder), Path.Combine(extracted, folder));
                foreach (string folder in Enumerable.Reverse(backedUp))
                    Directory.Move(Path.Combine(backup, folder), Path.Combine(directory, folder));
            }
            catch (IOException ex)
            {
                preserveBackup = true;
                throw new IOException("Install failed during rollback. Recovery files are at " + staging, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                preserveBackup = true;
                throw new IOException("Install failed during rollback. Recovery files are at " + staging, ex);
            }
            throw;
        }
        finally
        {
            if (!preserveBackup)
            {
                try { Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

internal sealed class Manager : Form
{
    internal const string ProductTitle = "Dave's Discount Meats AddOn Manager";
    private readonly List<Addon> addons;
    private readonly FlowLayoutPanel tiles = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly RoundedButton addGitHub = new RoundedButton { Text = "Add from GitHub…", AutoSize = true };
    private const string SettingsKey = @"Software\DDMAddOnManager";
    private string addonDirectory = "";
    private readonly TableLayoutPanel folderRow = new TableLayoutPanel {
        Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
        Margin = new Padding(3, 2, 3, 10), Padding = new Padding(9, 2, 4, 2)
    };
    private readonly Label folder = new Label {
        Text = "No folder selected", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly RoundedButton[] themeButtons = { new RoundedButton { Text = "System" },
        new RoundedButton { Text = "Light" }, new RoundedButton { Text = "Dark" } };
    private string colorTheme = "System";
    private readonly RoundedButton browse = new RoundedButton { Text = "Browse…", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly RoundedButton refresh = new RoundedButton { Text = "Refresh Available", AutoSize = true };
    private readonly RoundedButton appUpdates = new RoundedButton { Text = "App updates", AutoSize = true };
    private readonly RoundedButton install = new RoundedButton { Text = "Install selected", AutoSize = true };
    private readonly Label details = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private Addon detailsAddon;
    private bool busy;
    private bool checkingUpdate;
    private Version availableUpdate;
    private string availableUpdateTag;
    private const string ManagerReleasePage = "https://github.com/thechainercygnus/DDMAddOnManager/releases/tag/";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal Manager(Addon[] catalog)
    {
        addons = new List<Addon>(catalog);
        LoadCustomAddons();
        Text = ProductTitle;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(760, 470);
        MinimumSize = new Size(680, 495);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (int height in new[] { 44, 55, 0, 54, 43, 26 })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) :
                new RowStyle(SizeType.Absolute, height));

        var title = new Label {
            Text = "AddOn Manager", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold)
        };
        layout.Controls.Add(title, 0, 0);
        var themeControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right };
        themeControls.Controls.Add(new Label { Text = "Theme", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        string savedTheme = RegistryValue(Registry.CurrentUser, SettingsKey, "Theme") as string;
        if (themeButtons.Any(button => button.Text == savedTheme))
            colorTheme = savedTheme;
        foreach (RoundedButton button in themeButtons)
        {
            button.AutoSize = true;
            button.MinimumSize = new Size(52, 30);
            button.Padding = new Padding(6, 0, 6, 0);
            button.Margin = new Padding(2, 0, 0, 0);
            button.AccessibleName = button.Text + " theme";
            button.Click += (sender, args) => ChangeTheme(button.Text);
            themeControls.Controls.Add(button);
        }
        layout.Controls.Add(themeControls, 1, 0);
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(new Label {
            Text = "WoW Forever AddOns folder", AutoSize = true, Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 10, 0)
        }, 0, 0);
        folderRow.Controls.Add(folder, 1, 0);
        folderRow.Controls.Add(browse, 2, 0);
        layout.Controls.Add(folderRow, 0, 1);
        layout.SetColumnSpan(folderRow, 2);

        foreach (Addon addon in addons)
            AddTile(addon);
        layout.Controls.Add(tiles, 0, 2);
        layout.SetColumnSpan(tiles, 2);
        layout.Controls.Add(details, 0, 3);
        layout.SetColumnSpan(details, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(install);
        actions.Controls.Add(refresh);
        actions.Controls.Add(addGitHub);
        actions.Controls.Add(appUpdates);
        layout.Controls.Add(actions, 0, 4);
        layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(status, 0, 5);
        layout.SetColumnSpan(status, 2);
        Controls.Add(layout);

        browse.Click += (sender, args) => PickFolder();
        appUpdates.Click += async (sender, args) =>
        {
            if (availableUpdate == null)
                await CheckManagerUpdate(true);
            else
                OfferManagerUpdate();
        };
        addGitHub.Click += async (sender, args) => await AddGitHubAddon();
        refresh.Click += async (sender, args) => await RefreshReleases();
        install.Click += async (sender, args) => await InstallSelected();
        FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
        Shown += async (sender, args) =>
        {
            ApplyTheme();
            busy = true;
            UpdateButtons();
            Task updateCheck = CheckManagerUpdate(false);
            string detected = await Task.Run((Func<string>)DetectAddonDirectory);
            if (IsDisposed)
                return;
            if (addonDirectory.Length == 0 && detected != null)
                UseAddonDirectory(detected);
            await RefreshReleases();
            await updateCheck;
        };
        detailsAddon = addons[0];
        ShowDetails(detailsAddon);
        UpdateButtons();
        ApplyTheme();
    }

    private void AddTile(Addon addon)
    {
        addon.Tile = new AddonTile(addon);
        addon.Tile.Click += (sender, args) => ToggleAddon(addon);
        tiles.Controls.Add(addon.Tile);
    }

    private static bool ValidFolder(string folder)
    {
        return Regex.IsMatch(folder ?? "", @"\A[A-Za-z0-9_][A-Za-z0-9_-]*\z") &&
            !Regex.IsMatch(folder, @"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase);
    }

    private static bool ValidRepository(string repository)
    {
        return Regex.IsMatch(repository ?? "", @"\A[A-Za-z0-9-]+/[A-Za-z0-9_.-]+\z") &&
            !repository.EndsWith("/.", StringComparison.Ordinal) &&
            !repository.EndsWith("/..", StringComparison.Ordinal);
    }

    private bool ConflictsWithCatalog(Addon addon)
    {
        return addons.Any(existing =>
            existing.Folders.Intersect(addon.Folders, StringComparer.OrdinalIgnoreCase).Any() ||
            String.Equals(existing.Repository, addon.Repository, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadCustomAddons()
    {
        string json = RegistryValue(Registry.CurrentUser, SettingsKey, "CustomAddons") as string;
        if (String.IsNullOrEmpty(json))
            return;

        Addon[] saved;
        try { saved = new JavaScriptSerializer().Deserialize<Addon[]>(json); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }

        foreach (Addon addon in saved ?? new Addon[0])
        {
            if (addon == null || !ValidFolder(addon.Folder) || !ValidRepository(addon.Repository) ||
                String.IsNullOrWhiteSpace(addon.Name) || ConflictsWithCatalog(addon))
                continue;
            addon.Custom = true;
            addons.Add(addon);
        }
    }

    private static string RepositoryFromLink(string link)
    {
        Uri url;
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out url) || url.Scheme != Uri.UriSchemeHttps ||
            !String.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            url.UserInfo.Length != 0 || !url.IsDefaultPort)
            return null;

        string[] parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2 || (parts.Length > 2 && parts[2] != "releases"))
            return null;
        string name = parts[1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        string repository = parts[0] + "/" + name;
        return ValidRepository(repository) ? repository : null;
    }

    private async Task AddGitHubAddon()
    {
        using (var dialog = new Form {
            Text = "Add from GitHub", Font = Font, ClientSize = new Size(475, 213),
            BackColor = BackColor, ForeColor = ForeColor, FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = false
        })
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14),
                ColumnCount = 1, RowCount = 6 };
            foreach (int height in new[] { 23, 32, 23, 32, 36, 37 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var link = new TextBox { Dock = DockStyle.Fill, AccessibleName = "GitHub repository link",
                BackColor = BackColor, ForeColor = ForeColor };
            var folderInput = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Add-on folder name",
                BackColor = BackColor, ForeColor = ForeColor };
            var error = new Label { Dock = DockStyle.Fill };
            var add = new Button { Text = "Add", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(add);
            layout.Controls.Add(new Label { Text = "GitHub repository link", AutoSize = true }, 0, 0);
            layout.Controls.Add(link, 0, 1);
            layout.Controls.Add(new Label { Text = "Add-on folder name (optional)", AutoSize = true }, 0, 2);
            layout.Controls.Add(folderInput, 0, 3);
            layout.Controls.Add(error, 0, 4);
            layout.Controls.Add(buttons, 0, 5);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = add;
            dialog.CancelButton = cancel;

            Addon entry = null;
            add.Click += (sender, args) =>
            {
                string repository = RepositoryFromLink(link.Text);
                if (repository == null)
                {
                    error.Text = "Enter an HTTPS GitHub repository or release link.";
                    return;
                }
                string name = repository.Substring(repository.IndexOf('/') + 1);
                string folderName = folderInput.Text.Trim();
                if (folderName.Length == 0)
                    folderName = name;
                if (!ValidFolder(folderName))
                {
                    error.Text = "Enter the add-on folder name using letters, numbers, underscores, or hyphens.";
                    return;
                }
                entry = new Addon { Name = name, Folder = folderName, Repository = repository, Custom = true };
                if (entry.IsQuestieBundle || entry.IsQuestieDatabase)
                {
                    entry.Folder = entry.Folders[0];
                    entry.Name = entry.IsQuestieBundle ? "Questie + QuestieDB" : "QuestieDB";
                }
                if (ConflictsWithCatalog(entry))
                {
                    error.Text = "That repository or add-on folder is already in the manager.";
                    return;
                }
                dialog.DialogResult = DialogResult.OK;
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                string json = new JavaScriptSerializer().Serialize(addons.Where(addon => addon.Custom)
                    .Concat(new[] { entry }).ToArray());
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("CustomAddons", json);
            }
            catch (UnauthorizedAccessException) { status.Text = "Could not save the add-on."; return; }
            catch (IOException) { status.Text = "Could not save the add-on."; return; }
            catch (System.Security.SecurityException) { status.Text = "Could not save the add-on."; return; }
            catch (InvalidOperationException) { status.Text = "Could not save the add-on."; return; }

            addons.Add(entry);
            AddTile(entry);
            detailsAddon = entry;
            ApplyTheme();
            tiles.ScrollControlIntoView(entry.Tile);
            await RefreshReleases(entry);
        }
    }

    private void ChangeTheme(string choice)
    {
        if (colorTheme == choice)
            return;

        colorTheme = choice;
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue("Theme", choice);
        }
        catch (UnauthorizedAccessException) { status.Text = "Could not save the theme preference."; }
        catch (IOException) { status.Text = "Could not save the theme preference."; }
        catch (System.Security.SecurityException) { status.Text = "Could not save the theme preference."; }
        ApplyTheme();
    }

    private static object RegistryValue(RegistryKey root, string path, string name)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey(path))
                return key == null ? null : key.GetValue(name);
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
        catch (System.Security.SecurityException) { return null; }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // Windows broadcasts settings, system color and theme changes.
        if ((message.Msg == 0x001A || message.Msg == 0x0015 || message.Msg == 0x031A)
            && detailsAddon != null)
            ApplyTheme();
    }

    private void ApplyTheme()
    {
        object systemLight = RegistryValue(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme");
        bool dark = colorTheme == "Dark" || (colorTheme == "System" && Object.Equals(systemLight, 0));
        bool highContrast = SystemInformation.HighContrast;
        BackColor = highContrast ? SystemColors.Window : Gray(dark ? 32 : 243);
        ForeColor = highContrast ? SystemColors.WindowText : Gray(dark ? 248 : 27);
        Color surface = highContrast ? SystemColors.Control : Gray(dark ? 45 : 251);
        Color hover = highContrast ? surface : Gray(dark ? 54 : 247);
        Color border = highContrast ? SystemColors.WindowText : Gray(dark ? 64 : 218);
        Color secondary = highContrast ? ForeColor : Gray(dark ? 191 : 92);
        Color accent = highContrast ? SystemColors.Highlight :
            dark ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 95, 184);
        Color accentHover = highContrast ? accent :
            dark ? Color.FromArgb(115, 209, 253) : Color.FromArgb(25, 111, 192);
        foreach (RoundedButton button in themeButtons)
        {
            bool active = button.Text == colorTheme;
            button.BackColor = active ? hover : surface;
            button.ForeColor = ForeColor;
            button.BorderColor = active ? accent : border;
            button.HoverColor = hover;
            button.AccessibleDescription = active ? "Selected" : "";
            button.Invalidate();
        }
        folderRow.BackColor = highContrast ? SystemColors.Window : Gray(dark ? 39 : 238);
        folder.ForeColor = status.ForeColor = secondary;
        foreach (RoundedButton button in new[] { browse, refresh, install, addGitHub, appUpdates })
        {
            bool primary = button == install;
            button.Padding = new Padding(10, 3, 10, 3);
            button.BackColor = primary ? accent : surface;
            button.ForeColor = primary ? (highContrast ? SystemColors.HighlightText : dark ? Gray(20) : Color.White) : ForeColor;
            button.BorderColor = border;
            button.HoverColor = primary ? accentHover : hover;
            button.DisabledSurface = surface;
            button.DisabledText = highContrast ? SystemColors.GrayText : Gray(dark ? 147 : 109);
            button.Invalidate();
        }
        foreach (Addon addon in addons)
        {
            addon.Tile.Accent = accent;
            addon.Tile.BorderColor = border;
            addon.Tile.BackColor = surface;
            addon.Tile.HoverColor = hover;
            addon.Tile.ForeColor = ForeColor;
            addon.Tile.SecondaryText = secondary;
            addon.Tile.Invalidate();
        }
        if (IsHandleCreated)
        {
            int value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref value, sizeof(int));
            int round = 2; // DWMWCP_ROUND on Windows 11.
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }
    }

    private static Color Gray(int value)
    {
        return Color.FromArgb(value, value, value);
    }

    private void ToggleAddon(Addon addon)
    {
        addon.Tile.Selected = !addon.Tile.Selected;
        detailsAddon = addon;
        ShowDetails(addon);
        UpdateButtons();
    }
    private void ShowDetails(Addon addon)
    {
        AddonTile tile = addon.Tile;
        tile.AccessibleDescription = (tile.Selected ? "Selected. " : "Unselected. ") + tile.State;
        tile.Invalidate();
        if (detailsAddon == addon)
            details.Text = addon.Name + "  •  Installed: " + tile.Installed +
                "  •  GitHub: " + (String.IsNullOrEmpty(tile.Release) ? "—" : tile.Release) +
                Environment.NewLine + tile.State;
    }
    private Addon[] InstallableSelection()
    {
        return addons.Where(addon => addon.Tile.Selected && Package(addon) != null).ToArray();
    }

    private void UpdateButtons()
    {
        int count = InstallableSelection().Length;
        browse.Enabled = refresh.Enabled = addGitHub.Enabled = !busy;
        appUpdates.Enabled = !busy && !checkingUpdate;
        install.Text = count == 0 ? "Install selected" :
            count == 1 ? "Install 1 add-on" : "Install " + count + " add-ons";
        install.Enabled = !busy && addonDirectory.Length > 0 && count > 0;
    }
    private static string ForeverAddonDirectory(string path)
    {
        if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return null;
        try
        {
            string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!String.Equals(Path.GetFileName(root), "_forever_", StringComparison.OrdinalIgnoreCase))
                root = Path.Combine(root + Path.DirectorySeparatorChar, "_forever_");
            if (File.Exists(Path.Combine(root, "Wow.exe")) || File.Exists(Path.Combine(root, "WowArm64.exe")))
                return Path.Combine(root, "Interface", "AddOns");
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        catch (PathTooLongException) { }
        return null;
    }

    private static string DetectAddonDirectory()
    {
        string saved = RegistryValue(Registry.CurrentUser, SettingsKey, "AddonDirectory") as string;
        if (Directory.Exists(saved))
            return saved;

        const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
        const string blizzard = @"Software\Blizzard Entertainment\World of Warcraft";
        var candidates = new List<string>();
        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            {
                candidates.Add(RegistryValue(root, uninstall + "World of Warcraft", "InstallLocation") as string);
                candidates.Add(RegistryValue(root, uninstall + "World of Warcraft Forever", "InstallLocation") as string);
                candidates.Add(RegistryValue(root, blizzard, "InstallPath") as string);
            }
        }
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed))
        {
            foreach (string prefix in new[] { "", "Program Files", "Program Files (x86)", "Games" })
                candidates.Add(Path.Combine(drive.Name, prefix, "World of Warcraft"));
        }

        string[] found = candidates.Select(ForeverAddonDirectory).Where(path => path != null)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    private void UseAddonDirectory(string path)
    {
        addonDirectory = path;
        folder.Text = path;
        foreach (Addon addon in addons)
            ShowInstalled(addon);
        UpdateButtons();
    }

    private void PickFolder()
    {
        using (var picker = new FolderBrowserDialog { ShowNewFolderButton = false,
            Description = "Select your WoW Forever folder or its Interface\\AddOns folder." })
        {
            if (Directory.Exists(addonDirectory))
                picker.SelectedPath = addonDirectory;
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;
            string path = picker.SelectedPath;
            string detected = ForeverAddonDirectory(path);
            string addonsPath = Path.Combine(path, "Interface", "AddOns");
            if (Directory.Exists(addonsPath))
                path = addonsPath;
            if (detected == null && (!String.Equals(Path.GetFileName(path), "AddOns", StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(new DirectoryInfo(path).Parent.Name, "Interface", StringComparison.OrdinalIgnoreCase)))
            {
                status.Text = "Choose an existing Interface\\AddOns folder.";
                return;
            }
            UseAddonDirectory(detected ?? path);
            status.Text = "Select the add-ons you want to install or update.";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("AddonDirectory", addonDirectory);
            }
            catch (UnauthorizedAccessException) { status.Text = "Could not save the folder preference."; }
            catch (IOException) { status.Text = "Could not save the folder preference."; }
            catch (System.Security.SecurityException) { status.Text = "Could not save the folder preference."; }
        }
    }

    private static Version NewerManagerVersion(Release release, Version installed)
    {
        Version published;
        if (release == null || !Regex.IsMatch(release.tag_name ?? "", @"\Av[0-9]+\.[0-9]+\.[0-9]+\z") ||
            !Version.TryParse(release.tag_name.Substring(1), out published) ||
            release.assets == null || !release.assets.Any(asset => asset != null &&
                String.Equals(asset.name, "DDMAddOnManager-Setup.exe", StringComparison.OrdinalIgnoreCase)))
            return null;

        var current = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
        return published > current ? published : null;
    }

    private async Task CheckManagerUpdate(bool interactive)
    {
        if (checkingUpdate)
            return;

        checkingUpdate = true;
        UpdateButtons();
        try
        {
            const string endpoint = "https://api.github.com/repos/thechainercygnus/DDMAddOnManager/releases/latest";
            Release release;
            using (var client = GitHubClient())
            {
                client.Headers["Accept"] = "application/vnd.github+json";
                string json = await client.DownloadStringTaskAsync(new Uri(endpoint));
                release = new JavaScriptSerializer().Deserialize<Release>(json);
            }

            if (IsDisposed || Disposing)
                return;

            availableUpdate = NewerManagerVersion(release, typeof(Manager).Assembly.GetName().Version);
            if (availableUpdate != null)
            {
                availableUpdateTag = release.tag_name;
                appUpdates.Text = "Update available";
                appUpdates.AccessibleDescription = "Version " + availableUpdate + " is available.";
                if (interactive)
                    OfferManagerUpdate();
            }
            else if (interactive)
            {
                MessageBox.Show(this, "No newer installer is available.", "App updates",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (WebException) { ShowUpdateCheckError(interactive); }
        catch (ArgumentException) { ShowUpdateCheckError(interactive); }
        catch (InvalidOperationException) { ShowUpdateCheckError(interactive); }
        finally
        {
            checkingUpdate = false;
            if (!IsDisposed && !Disposing)
                UpdateButtons();
        }
    }

    private void ShowUpdateCheckError(bool interactive)
    {
        if (interactive && !IsDisposed && !Disposing)
            MessageBox.Show(this, "Could not check GitHub for a manager update. Try again later.", "App updates",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OfferManagerUpdate()
    {
        if (MessageBox.Show(this, "Version " + availableUpdate + " is available. Open the release page to download Setup?",
            "App updates", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(ManagerReleasePage + availableUpdateTag) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Could not open your web browser. The release is at " +
                ManagerReleasePage + availableUpdateTag, "App updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }


    private static WebClient GitHubClient()
    {
        var client = new WebClient();
        client.Headers["User-Agent"] = "DDMAddOnManager/" + typeof(Manager).Assembly.GetName().Version.ToString(3);
        return client;
    }

    private static Asset Package(Addon addon)
    {
        if (addon.Latest == null || String.IsNullOrWhiteSpace(addon.Latest.tag_name) || addon.Latest.assets == null)
            return null;
        if (addon.IsQuestieBundle || addon.IsQuestieDatabase)
        {
            string[] versions = addon.ReleaseVersions(addon.Latest.tag_name);
            if (versions == null)
                return null;
            string expected = addon.IsQuestieBundle ?
                "Questie-v" + versions[0] + "+v" + versions[1] + ".zip" :
                "QuestieDB-" + versions[0].TrimStart('v', 'V') + "-Forever.zip";
            return addon.Latest.assets.FirstOrDefault(asset => asset != null &&
                String.Equals(asset.name, expected, StringComparison.OrdinalIgnoreCase));
        }

        Asset exact = addon.Latest.assets.FirstOrDefault(asset => asset != null &&
            String.Equals(asset.name, addon.Folder + ".zip", StringComparison.OrdinalIgnoreCase));
        if (exact != null || !addon.Custom)
            return exact;

        Asset[] zipAssets = addon.Latest.assets.Where(asset => asset != null && asset.name != null &&
            asset.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return zipAssets.Length == 1 ? zipAssets[0] : null;
    }

    private void ShowInstalled(Addon addon)
    {
        string[] folders = addon.Folders;
        var installed = new string[folders.Length];
        bool anyInstalled = false;
        if (addonDirectory.Length > 0)
        {
            for (int index = 0; index < folders.Length; index++)
            {
                string folder = folders[index];
                string path = Path.Combine(addonDirectory, folder);
                anyInstalled |= Directory.Exists(path);
                try { installed[index] = AddonFiles.VersionAt(path, addon.Manifest(folder)); }
                catch (IOException) { installed[index] = "Unreadable"; }
                catch (UnauthorizedAccessException) { installed[index] = "Unreadable"; }
            }
        }
        addon.Tile.Installed = addonDirectory.Length == 0 ? "Select folder" :
            !anyInstalled && Package(addon) != null ? "Not installed" :
            String.Join(" + ", installed.Select(version => version ?? "Unknown"));
        if (Package(addon) != null)
        {
            string[] latest = addon.ReleaseVersions(addon.Latest.tag_name);
            addon.Tile.State = addonDirectory.Length == 0 ? "" :
                installed.Zip(latest, AddonFiles.SameVersion).All(matches => matches) ? "Current" :
                anyInstalled ? "Version differs" : "Ready to install";
        }
        ShowDetails(addon);
    }

    private async Task RefreshReleases(Addon only = null)
    {
        busy = true;
        UpdateButtons();
        status.Text = "Checking GitHub for releases...";
        try
        {
            foreach (Addon addon in only == null ? (IEnumerable<Addon>)addons : new[] { only })
            {
                addon.Latest = null;
                addon.Tile.Release = "";
                addon.Tile.State = "Checking...";
                ShowDetails(addon);
                if (String.IsNullOrWhiteSpace(addon.Repository))
                {
                    addon.Tile.State = "Repo pending";
                    ShowInstalled(addon);
                    continue;
                }
                try
                {
                    using (var client = GitHubClient())
                    {
                        string endpoint = "https://api.github.com/repos/" + addon.Repository + "/releases/latest";
                        client.Headers["Accept"] = "application/vnd.github+json";
                        string json = await client.DownloadStringTaskAsync(new Uri(endpoint));
                        addon.Latest = new JavaScriptSerializer().Deserialize<Release>(json);
                    }
                    addon.Tile.Release = addon.Latest == null ? "" : addon.Latest.tag_name;
                    if (Package(addon) == null)
                        addon.Tile.State = "ZIP missing";
                }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    addon.Tile.State = response == null ? "Connection failed" :
                        response.StatusCode == HttpStatusCode.NotFound ? "No public release" :
                        "GitHub HTTP " + (int)response.StatusCode;
                }
                catch (ArgumentException)
                {
                    addon.Tile.State = "Invalid response";
                }
                ShowInstalled(addon);
            }
            status.Text = "Choose your game folder and select the add-ons you want to install.";
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
    }

    private async Task InstallSelected()
    {
        Addon[] batch = InstallableSelection();
        if (busy || addonDirectory.Length == 0 || batch.Length == 0)
            return;

        string directory = addonDirectory;
        int unavailable = addons.Count(addon => addon.Tile.Selected) - batch.Length;
        string[] replacements = batch.Where(addon => addon.Folders.Any(folder => Directory.Exists(Path.Combine(directory, folder))))
            .Select(addon => addon.Name + " (" + addon.Latest.tag_name + ")").ToArray();
        if (replacements.Length > 0 &&
            MessageBox.Show(this, "Replace the installed files for these add-ons?\n\n" +
                String.Join("\n", replacements), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes)
            return;

        var failures = new List<string>();
        int installed = 0;
        busy = true;
        UpdateButtons();
        try
        {
            foreach (Addon addon in batch)
            {
                try
                {
                    status.Text = "Installing " + (installed + failures.Count + 1) + " of " + batch.Length +
                        "  •  " + addon.Name + " " + addon.Latest.tag_name;
                    await InstallAddon(directory, addon);
                    installed++;
                }
                catch (Exception ex)
                {
                    failures.Add(addon.Name + ": " + ex.Message);
                }
                finally
                {
                    ShowInstalled(addon);
                }
            }

            string summary = installed + " of " + batch.Length + " installed.";
            if (failures.Count > 0)
                summary += " " + failures.Count + " failed.";
            if (unavailable > 0)
                summary += " " + unavailable + " unavailable.";
            if (installed > 0)
                summary += " Restart WoW Forever to load the changes.";
            status.Text = summary;
            if (failures.Count > 0)
                MessageBox.Show(this, summary + "\n\n" + String.Join("\n\n", failures),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
    }

    private static async Task InstallAddon(string directory, Addon addon)
    {
        Asset asset = Package(addon);
        string tag = addon.Latest.tag_name;
        var url = new Uri(asset.browser_download_url);
        if (url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com" ||
            !url.AbsolutePath.StartsWith("/" + addon.Repository + "/releases/download/",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected GitHub download URL.");

        string archive = Path.GetTempFileName();
        try
        {
            using (var client = GitHubClient())
                await client.DownloadFileTaskAsync(url, archive);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                AddonFiles.Install(directory, addon, archive, tag);
            });
        }
        finally
        {
            try { File.Delete(archive); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addons.json");
            string json;
            if (File.Exists(path))
                json = File.ReadAllText(path);
            else
                using (var reader = new StreamReader(typeof(Manager).Assembly.GetManifestResourceStream("addons.json")))
                    json = reader.ReadToEnd();
            var catalog = new JavaScriptSerializer().Deserialize<Addon[]>(json);
            if (catalog == null || catalog.Length == 0 || catalog.Any(addon => addon == null ||
                String.IsNullOrWhiteSpace(addon.Name) ||
                !ValidFolder(addon.Folder) ||
                (!String.IsNullOrEmpty(addon.Repository) &&
                    !ValidRepository(addon.Repository))) ||
                catalog.Select(addon => addon.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Length)
                throw new InvalidDataException("The addon catalog has an invalid or duplicate entry.");
            Application.Run(new Manager(catalog));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, ProductTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
