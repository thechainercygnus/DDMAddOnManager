using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Dave's Discount Meats AddOn Manager Setup")]
[assembly: AssemblyProduct("Dave's Discount Meats AddOn Manager")]

internal static class Setup
{
    private const string Product = "Dave's Discount Meats AddOn Manager";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DDMAddOnManager";

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        if (MessageBox.Show("Install " + Product + " for your Windows account?",
            Product, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return 0;

        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "DDMAddOnManager");
            string executable = Path.Combine(directory, Product + ".exe");
            string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product + ".lnk");
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.GetDirectoryName(shortcut));
            InstallExecutable(executable);
            CreateShortcut(shortcut, executable);
            RegisterUninstall(directory, executable, shortcut);
            if (MessageBox.Show("Installation complete. Open the AddOn Manager now?",
                Product, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(executable);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Setup could not finish. Close the AddOn Manager if it is open, then run Setup again.\n\n" +
                ex.Message, Product, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static void InstallExecutable(string executable)
    {
        string pending = executable + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var payload = typeof(Setup).Assembly.GetManifestResourceStream("AddOnManager.exe"))
            using (var file = new FileStream(pending, FileMode.CreateNew))
                payload.CopyTo(file);
            if (File.Exists(executable))
                File.Replace(pending, executable, null);
            else
                File.Move(pending, executable);
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
    }

    internal static void CreateShortcut(string path, string executable)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            try
            {
                shortcut.TargetPath = executable;
                shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
                shortcut.Description = Product;
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string QuotePowerShell(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    internal static string UninstallScript(string directory, string executable, string shortcut, string registryKey)
    {
        return "$ErrorActionPreference = 'Stop'\n" +
            "try {\n" +
            "  foreach ($path in @(" + QuotePowerShell(executable) + ", " + QuotePowerShell(shortcut) + ")) {\n" +
            "    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }\n" +
            "  }\n" +
            "  if ([IO.Directory]::Exists(" + QuotePowerShell(directory) + ") -and " +
            "[IO.Directory]::GetFileSystemEntries(" + QuotePowerShell(directory) + ").Length -eq 0) {\n" +
            "    [IO.Directory]::Delete(" + QuotePowerShell(directory) + ")\n" +
            "  }\n" +
            "  if (Test-Path -LiteralPath " + QuotePowerShell(registryKey) + ") {\n" +
            "    Remove-Item -LiteralPath " + QuotePowerShell(registryKey) + " -Force\n" +
            "  }\n" +
            "} catch {\n" +
            "  Add-Type -AssemblyName System.Windows.Forms\n" +
            "  [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, " + QuotePowerShell(Product) + ")\n" +
            "  exit 1\n" +
            "}\n";
    }

    private static void RegisterUninstall(string directory, string executable, string shortcut)
    {
        string script = UninstallScript(directory, executable, shortcut, @"HKCU:\" + UninstallKey);
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", Product);
            key.SetValue("Publisher", "Dave's Discount Meats");
            key.SetValue("DisplayVersion", typeof(Setup).Assembly.GetName().Version.ToString(3));
            key.SetValue("DisplayIcon", executable);
            key.SetValue("InstallLocation", directory);
            key.SetValue("NoModify", 1);
            key.SetValue("NoRepair", 1);
            key.SetValue("UninstallString", "\"" + powershell + "\" -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded);
        }
    }
}
