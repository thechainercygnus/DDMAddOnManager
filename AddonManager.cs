using System;
using System.Collections.Generic;
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
    public Release Latest;
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
    internal static string VersionAt(string directory, string folder)
    {
        string manifest = Path.Combine(directory, folder + ".toc");
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
        string destination = Path.Combine(directory, addon.Folder);
        if (Directory.Exists(destination) &&
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The installed addon folder is a link.");

        string staging = Path.Combine(directory, ".addon-manager-" + Guid.NewGuid().ToString("N"));
        string extracted = Path.Combine(staging, "package");
        string backup = Path.Combine(staging, "previous");
        Directory.CreateDirectory(staging);
        bool preserveBackup = false;
        try
        {
            ZipFile.ExtractToDirectory(archive, extracted);
            string source = Path.Combine(extracted, addon.Folder);
            if (!SameVersion(VersionAt(source, addon.Folder), tag))
                throw new InvalidDataException("The ZIP must contain " + addon.Folder +
                    "/" + addon.Folder + ".toc with a version matching " + tag + ".");

            bool hasPrevious = Directory.Exists(destination);
            if (hasPrevious)
                Directory.Move(destination, backup);
            try
            {
                Directory.Move(source, destination);
            }
            catch
            {
                if (hasPrevious)
                {
                    preserveBackup = true;
                    try { Directory.Move(backup, destination); }
                    catch (IOException ex)
                    {
                        throw new IOException("Install failed. Previous files are at " + backup, ex);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        throw new IOException("Install failed. Previous files are at " + backup, ex);
                    }
                    preserveBackup = false;
                }
                throw;
            }
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
    private readonly Addon[] addons;
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
    private readonly RoundedButton install = new RoundedButton { Text = "Install selected", AutoSize = true };
    private readonly Label details = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private Addon detailsAddon;
    private bool busy;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal Manager(Addon[] catalog)
    {
        addons = catalog;
        Text = ProductTitle;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(760, 470);
        MinimumSize = new Size(605, 495);
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

        var tiles = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        foreach (Addon addon in addons)
        {
            addon.Tile = new AddonTile(addon);
            addon.Tile.Click += (sender, args) => ToggleAddon(addon);
            tiles.Controls.Add(addon.Tile);
        }
        layout.Controls.Add(tiles, 0, 2);
        layout.SetColumnSpan(tiles, 2);
        layout.Controls.Add(details, 0, 3);
        layout.SetColumnSpan(details, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(install);
        actions.Controls.Add(refresh);
        layout.Controls.Add(actions, 0, 4);
        layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(status, 0, 5);
        layout.SetColumnSpan(status, 2);
        Controls.Add(layout);

        browse.Click += (sender, args) => PickFolder();
        refresh.Click += async (sender, args) => await RefreshReleases();
        install.Click += async (sender, args) => await InstallSelected();
        FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
        Shown += async (sender, args) =>
        {
            ApplyTheme();
            string detected = await Task.Run((Func<string>)DetectAddonDirectory);
            if (IsDisposed)
                return;
            if (addonDirectory.Length == 0 && detected != null)
                UseAddonDirectory(detected);
            if (!busy)
                await RefreshReleases();
        };
        detailsAddon = addons[0];
        ShowDetails(detailsAddon);
        UpdateButtons();
        ApplyTheme();
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
        foreach (RoundedButton button in new[] { browse, refresh, install })
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
        browse.Enabled = refresh.Enabled = !busy;
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

    private static WebClient GitHubClient()
    {
        var client = new WebClient();
        client.Headers["User-Agent"] = "DDMAddOnManager/" + typeof(Manager).Assembly.GetName().Version.ToString(3);
        return client;
    }

    private static Asset Package(Addon addon)
    {
        if (addon.Latest == null || addon.Latest.assets == null)
            return null;
        return addon.Latest.assets.FirstOrDefault(asset =>
            String.Equals(asset.name, addon.Folder + ".zip", StringComparison.OrdinalIgnoreCase));
    }

    private void ShowInstalled(Addon addon)
    {
        string version = null;
        if (addonDirectory.Length > 0)
        {
            try { version = AddonFiles.VersionAt(Path.Combine(addonDirectory, addon.Folder), addon.Folder); }
            catch (IOException) { version = "Unreadable"; }
            catch (UnauthorizedAccessException) { version = "Unreadable"; }
        }
        addon.Tile.Installed = version ?? (addonDirectory.Length == 0 ? "Select folder" : "Unknown");
        if (Package(addon) != null)
        {
            addon.Tile.State = addonDirectory.Length == 0 ? "" :
                AddonFiles.SameVersion(version, addon.Latest.tag_name) ? "Current" :
                Directory.Exists(Path.Combine(addonDirectory, addon.Folder)) ? "Version differs" :
                "Ready to install";
            if (addonDirectory.Length > 0 && !Directory.Exists(Path.Combine(addonDirectory, addon.Folder)))
                addon.Tile.Installed = "Not installed";
        }
        ShowDetails(addon);
    }

    private async Task RefreshReleases()
    {
        busy = true;
        UpdateButtons();
        status.Text = "Checking GitHub for releases...";
        try
        {
            foreach (Addon addon in addons)
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
                    addon.Tile.Release = addon.Latest.tag_name;
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
        string[] replacements = batch.Where(addon => Directory.Exists(Path.Combine(directory, addon.Folder)))
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
                !Regex.IsMatch(addon.Folder ?? "", @"\A[A-Za-z0-9_][A-Za-z0-9_-]*\z") ||
                (!String.IsNullOrEmpty(addon.Repository) &&
                    !Regex.IsMatch(addon.Repository, @"\A[A-Za-z0-9-]+/[A-Za-z0-9_.-]+\z"))) ||
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
