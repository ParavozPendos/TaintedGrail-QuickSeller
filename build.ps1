# Builds the mod in Release for both game runtimes and packs
#   dist\<Mod>-<version>-IL2CPP.zip  (BepInEx 6 IL2CPP, default Steam/GOG build)
#   dist\<Mod>-<version>-Mono.zip    (BepInEx 5, "mono" beta branch)
# Archive layout: BepInEx/plugins/<Mod>/<Mod>.dll — extract into the game folder.
# A runtime is built when its references are found (see local.props.example). Requires .NET SDK 6+.
# -Deploy also copies the build matching the game in GameDir into BepInEx\plugins.
param([string]$GameDir = "", [switch]$Deploy)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$mod = 'QuickSeller'
$proj = Join-Path $root "$mod.csproj"
$a = @('build', $proj, '-c', 'Release', '-nologo', '-v', 'q')
if (-not $Deploy) { $a += '-p:NoDeploy=true' }
if ($GameDir) { $a += "-p:GameDir=$GameDir" }
dotnet @a
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
$ver = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
foreach ($r in @(@{ Name = 'IL2CPP'; Tfm = 'net6.0' }, @{ Name = 'Mono'; Tfm = 'netstandard2.1' })) {
    $dll = Join-Path $root "bin\Release\$($r.Tfm)\$mod.dll"
    $zip = Join-Path $dist "$mod-$ver-$($r.Name).zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    if (-not (Test-Path $dll)) { "{0}: not built (references not found)" -f $r.Name; continue }
    $za = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $dll, "BepInEx/plugins/$mod/$mod.dll", 'Optimal') | Out-Null
    } finally { $za.Dispose() }
    "{0}  {1:N0} bytes" -f $zip, (Get-Item $zip).Length
}
