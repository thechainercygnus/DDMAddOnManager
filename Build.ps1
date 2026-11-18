[CmdletBinding()]
param (
    [ValidatePattern('\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}\z')]
    [string] $Version = '0.1.0',

    [switch] $AppOnly
)

$ErrorActionPreference = 'Stop'
$product = "Dave's Discount Meats AddOn Manager"
$dist = Join-Path $PSScriptRoot 'dist'
$app = Join-Path $dist $product
$assets = Join-Path $dist 'release-assets'
New-Item -ItemType Directory -Path $app -Force | Out-Null

$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe = Join-Path $app "$product.exe"
$catalogPath = Join-Path $PSScriptRoot 'addons.json'
$versionSource = Join-Path $dist 'Version.cs'
@"
[assembly: System.Reflection.AssemblyVersion("$Version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$Version.0")]
"@ | Set-Content -LiteralPath $versionSource
& $compiler /nologo /target:winexe "/out:$exe" "/resource:$catalogPath,addons.json" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'AddonManager.cs') (Join-Path $PSScriptRoot 'AddonTile.cs') $versionSource
if ($LASTEXITCODE -ne 0) {
    throw 'The application build failed.'
}

Copy-Item -LiteralPath $catalogPath -Destination $app
$installer = Join-Path $dist 'DDMAddOnManager-Setup.exe'
$manifest = Join-Path $PSScriptRoot 'Setup.manifest'
& $compiler /nologo /target:winexe "/out:$installer" "/resource:$exe,AddOnManager.exe" "/win32manifest:$manifest" /reference:System.Windows.Forms.dll /reference:Microsoft.CSharp.dll (Join-Path $PSScriptRoot 'Setup.cs') $versionSource
if ($LASTEXITCODE -ne 0) {
    throw 'The installer build failed.'
}

if (!$AppOnly) {
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    foreach ($addon in $catalog) {
        $source = Join-Path (Split-Path -Parent $PSScriptRoot) $addon.Folder $addon.Folder
        if (!(Test-Path -LiteralPath (Join-Path $source ($addon.Folder + '.toc')) -PathType Leaf)) {
            throw "Missing addon manifest in $source."
        }
        Compress-Archive -LiteralPath $source -DestinationPath (Join-Path $assets ($addon.Folder + '.zip')) -Force
    }
}

$distribution = Join-Path $dist "$product.zip"
Compress-Archive -LiteralPath $exe, (Join-Path $app 'addons.json') -DestinationPath $distribution -Force
Get-Item -LiteralPath $installer, $distribution
