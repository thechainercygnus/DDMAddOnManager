using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle(Manager.ProductTitle)]
[assembly: AssemblyProduct(Manager.ProductTitle)]

internal sealed class Addon
{
    public string Folder { get; set; }
    public string Name { get; set; }
    public string Repository { get; set; }
    public Release Latest;
    public ListViewItem Row;
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
    private readonly TextBox folder = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ListView list = new ListView {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false
    };
    private readonly Button browse = new Button { Text = "Browse...", AutoSize = true };
    private readonly Button refresh = new Button { Text = "Check GitHub", AutoSize = true };
    private readonly Button install = new Button { Text = "Install selected", AutoSize = true };
    private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private bool busy;

    internal Manager(Addon[] catalog)
    {
        addons = catalog;
        Text = ProductTitle;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(830, 410);
        MinimumSize = new Size(710, 360);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(new Label { Text = "WoW Forever AddOns folder", AutoSize = true }, 0, 0);
        layout.Controls.Add(folder, 0, 1);
        layout.Controls.Add(browse, 1, 1);
        foreach (var column in new[] { "Addon", "Installed", "GitHub release", "Status" })
            list.Columns.Add(column, column == "Status" ? 290 : column == "Addon" ? 220 : 115);
        foreach (Addon addon in addons)
        {
            addon.Row = new ListViewItem(new[] { addon.Name, "", "", "" }) { Tag = addon };
            list.Items.Add(addon.Row);
        }
        layout.Controls.Add(list, 0, 2);
        layout.SetColumnSpan(list, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
        actions.Controls.Add(refresh);
        actions.Controls.Add(install);
        layout.Controls.Add(actions, 0, 3);
        layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(status, 0, 4);
        layout.SetColumnSpan(status, 2);
        Controls.Add(layout);

        browse.Click += (sender, args) => PickFolder();
        refresh.Click += async (sender, args) => await RefreshReleases();
        install.Click += async (sender, args) => await InstallSelected();
        list.SelectedIndexChanged += (sender, args) => UpdateButtons();
        FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
        Shown += async (sender, args) => await RefreshReleases();
        UpdateButtons();
    }

    private Addon Selected
    {
        get { return list.SelectedItems.Count == 0 ? null : (Addon)list.SelectedItems[0].Tag; }
    }

    private void UpdateButtons()
    {
        Addon addon = Selected;
        browse.Enabled = refresh.Enabled = !busy;
        install.Enabled = !busy && folder.Text.Length > 0 && addon != null && Package(addon) != null;
    }

    private void PickFolder()
    {
        using (var picker = new FolderBrowserDialog { ShowNewFolderButton = false,
            Description = "Select your WoW Forever folder or its Interface\\AddOns folder." })
        {
            if (folder.Text.Length > 0)
                picker.SelectedPath = folder.Text;
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
        if (folder.Text.Length > 0)
        {
            try { version = AddonFiles.VersionAt(Path.Combine(folder.Text, addon.Folder), addon.Folder); }
            catch (IOException) { version = "Unreadable"; }
            catch (UnauthorizedAccessException) { version = "Unreadable"; }
        }
        addon.Row.SubItems[1].Text = version ?? (folder.Text.Length == 0 ? "Select folder" : "Unknown");
        if (Package(addon) != null)
        {
            addon.Row.SubItems[3].Text = folder.Text.Length == 0 ? "Choose game folder" :
                AddonFiles.SameVersion(version, addon.Latest.tag_name) ? "Current" :
                Directory.Exists(Path.Combine(folder.Text, addon.Folder)) ? "Installed version differs" :
                "Ready to install";
            if (folder.Text.Length > 0 && !Directory.Exists(Path.Combine(folder.Text, addon.Folder)))
                addon.Row.SubItems[1].Text = "Not installed";
        }
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
                addon.Row.SubItems[2].Text = "";
                addon.Row.SubItems[3].Text = "Checking...";
                if (String.IsNullOrWhiteSpace(addon.Repository))
                {
                    addon.Row.SubItems[3].Text = "GitHub repository pending";
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
                    addon.Row.SubItems[2].Text = addon.Latest.tag_name;
                    if (Package(addon) == null)
                        addon.Row.SubItems[3].Text = "Missing " + addon.Folder + ".zip";
                }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    addon.Row.SubItems[3].Text = response == null ? "GitHub connection failed" :
                        response.StatusCode == HttpStatusCode.NotFound ? "No public release found" :
                        "GitHub returned HTTP " + (int)response.StatusCode;
                }
                catch (ArgumentException)
                {
                    addon.Row.SubItems[3].Text = "Invalid GitHub response";
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
        string directory = folder.Text;
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
