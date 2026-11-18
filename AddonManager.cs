using System;
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
    private readonly Label folder = new Label {
        Text = "No folder selected", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly ComboBox theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 104 };
    private readonly Button browse = new Button { Text = "Browse…", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    private readonly Button refresh = new Button { Text = "Check GitHub", AutoSize = true };
    private readonly Button install = new Button { Text = "Install selected", AutoSize = true };
    private readonly Label details = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private Addon Selected;
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
            Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (int height in new[] { 44, 26, 42, 0, 54, 43, 26 })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) :
                new RowStyle(SizeType.Absolute, height));

        var title = new Label {
            Text = "AddOn Manager", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold)
        };
        layout.Controls.Add(title, 0, 0);
        var themeControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right };
        themeControls.Controls.Add(new Label { Text = "Theme", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        theme.AccessibleName = "Color theme";
        theme.Items.AddRange(new object[] { "System", "Light", "Dark" });
        string savedTheme = RegistryValue(Registry.CurrentUser, SettingsKey, "Theme") as string;
        theme.SelectedIndex = Math.Max(0, theme.Items.IndexOf(savedTheme ?? ""));
        themeControls.Controls.Add(theme);
        layout.Controls.Add(themeControls, 1, 0);
        layout.Controls.Add(new Label { Text = "WoW Forever AddOns folder", AutoSize = true }, 0, 1);
        layout.Controls.Add(folder, 0, 2);
        layout.Controls.Add(browse, 1, 2);

        var tiles = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        foreach (Addon addon in addons)
        {
            addon.Tile = new AddonTile(addon);
            addon.Tile.Click += (sender, args) => SelectAddon(addon);
            tiles.Controls.Add(addon.Tile);
        }
        layout.Controls.Add(tiles, 0, 3);
        layout.SetColumnSpan(tiles, 2);
        layout.Controls.Add(details, 0, 4);
        layout.SetColumnSpan(details, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(install);
        actions.Controls.Add(refresh);
        layout.Controls.Add(actions, 0, 5);
        layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(status, 0, 6);
        layout.SetColumnSpan(status, 2);
        Controls.Add(layout);

        browse.Click += (sender, args) => PickFolder();
        refresh.Click += async (sender, args) => await RefreshReleases();
        install.Click += async (sender, args) => await InstallSelected();
        theme.SelectedIndexChanged += (sender, args) =>
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("Theme", theme.SelectedItem.ToString());
            }
            catch (UnauthorizedAccessException) { status.Text = "Could not save the theme preference."; }
            catch (IOException) { status.Text = "Could not save the theme preference."; }
            catch (System.Security.SecurityException) { status.Text = "Could not save the theme preference."; }
            ApplyTheme();
        };
        FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
        Shown += async (sender, args) => { ApplyTheme(); await RefreshReleases(); };
        SelectAddon(addons[0]);
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
            && addons != null && theme != null && theme.SelectedIndex >= 0)
            ApplyTheme();
    }

    private void ApplyTheme()
    {
        object systemLight = RegistryValue(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme");
        bool dark = theme.SelectedIndex == 2 || (theme.SelectedIndex == 0 && Object.Equals(systemLight, 0));
        BackColor = dark ? Color.FromArgb(20, 26, 37) : Color.FromArgb(241, 245, 251);
        ForeColor = dark ? Color.FromArgb(234, 240, 249) : Color.FromArgb(23, 35, 55);
        Color surface = dark ? Color.FromArgb(33, 42, 57) : Color.White;
        Color accent = dark ? Color.FromArgb(145, 187, 255) : Color.FromArgb(38, 88, 183);
        theme.BackColor = surface;
        theme.ForeColor = ForeColor;
        foreach (Button button in new[] { browse, refresh, install })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Padding = new Padding(8, 2, 8, 2);
            button.BackColor = button == install ? accent : surface;
            button.ForeColor = button == install ? (dark ? BackColor : Color.White) : ForeColor;
            button.FlatAppearance.BorderColor = accent;
        }
        foreach (Addon addon in addons)
        {
            addon.Tile.Dark = dark;
            addon.Tile.Accent = accent;
            addon.Tile.BackColor = surface;
            addon.Tile.ForeColor = ForeColor;
            addon.Tile.Invalidate();
        }
        if (IsHandleCreated)
        {
            int value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref value, sizeof(int));
        }
    }

    private void SelectAddon(Addon addon)
    {
        if (Selected != null)
        {
            Selected.Tile.Selected = false;
            Selected.Tile.Invalidate();
        }
        Selected = addon;
        addon.Tile.Selected = true;
        ShowDetails(addon);
        UpdateButtons();
    }

    private void ShowDetails(Addon addon)
    {
        AddonTile tile = addon.Tile;
        tile.AccessibleDescription = tile.State;
        tile.Invalidate();
        if (Selected == addon)
            details.Text = addon.Name + "  •  Installed: " + tile.Installed +
                "  •  GitHub: " + (String.IsNullOrEmpty(tile.Release) ? "—" : tile.Release) +
                Environment.NewLine + tile.State;
    }
    private void UpdateButtons()
    {
        Addon addon = Selected;
        browse.Enabled = refresh.Enabled = !busy;
        install.Enabled = !busy && addonDirectory.Length > 0 && addon != null && Package(addon) != null;
    }

    private void PickFolder()
    {
        using (var picker = new FolderBrowserDialog { ShowNewFolderButton = false,
            Description = "Select your WoW Forever folder or its Interface\\AddOns folder." })
        {
            if (addonDirectory.Length > 0)
                picker.SelectedPath = addonDirectory;
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;
            string path = picker.SelectedPath;
            string addonsPath = Path.Combine(path, "Interface", "AddOns");
            if (Directory.Exists(addonsPath))
                path = addonsPath;
            if (!String.Equals(Path.GetFileName(path), "AddOns", StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(new DirectoryInfo(path).Parent.Name, "Interface", StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "Choose an existing Interface\\AddOns folder.";
                return;
            }
            addonDirectory = path;
            folder.Text = path;
            foreach (Addon addon in addons)
                ShowInstalled(addon);
            status.Text = "Select an addon to install or update.";
            UpdateButtons();
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
            addon.Tile.State = addonDirectory.Length == 0 ? "Choose game folder" :
                AddonFiles.SameVersion(version, addon.Latest.tag_name) ? "Current" :
                Directory.Exists(Path.Combine(addonDirectory, addon.Folder)) ? "Installed version differs" :
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
                    addon.Tile.State = "GitHub repository pending";
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
                        addon.Tile.State = "Missing " + addon.Folder + ".zip";
                }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    addon.Tile.State = response == null ? "GitHub connection failed" :
                        response.StatusCode == HttpStatusCode.NotFound ? "No public release found" :
                        "GitHub returned HTTP " + (int)response.StatusCode;
                }
                catch (ArgumentException)
                {
                    addon.Tile.State = "Invalid GitHub response";
                }
                ShowInstalled(addon);
            }
            status.Text = "Choose your game folder and select an available addon.";
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
    }

    private async Task InstallSelected()
    {
        Addon addon = Selected;
        Asset asset = Package(addon);
        string directory = addonDirectory;
        string tag = addon.Latest.tag_name;
        if (Directory.Exists(Path.Combine(directory, addon.Folder)) &&
            MessageBox.Show(this, "Replace the installed " + addon.Name + " files with " + tag + "?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        string archive = null;
        busy = true;
        UpdateButtons();
        status.Text = "Downloading " + addon.Name + " " + tag + "...";
        try
        {
            var url = new Uri(asset.browser_download_url);
            if (url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com" ||
                !url.AbsolutePath.StartsWith("/" + addon.Repository + "/releases/download/",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected GitHub download URL.");
            archive = Path.GetTempFileName();
            using (var client = GitHubClient())
                await client.DownloadFileTaskAsync(url, archive);
            await Task.Run(() => AddonFiles.Install(directory, addon, archive, tag));
            status.Text = "Installed " + addon.Name + " " + tag + ". Restart WoW Forever to load it.";
        }
        catch (Exception ex)
        {
            status.Text = "Installation failed.";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (archive != null)
            {
                try { File.Delete(archive); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            ShowInstalled(addon);
            busy = false;
            UpdateButtons();
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
