param(
    [string]$Configuration = "Release",
    [string]$Version = "2.0.0",
    [string]$PclHostRoot = "",
    [string]$PclCorePath = "",
    [string]$OutputDir = "artifacts"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PclHostRoot)) {
    $PclHostRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "..\PCL2-Nex"))
} else {
    $PclHostRoot = [IO.Path]::GetFullPath($PclHostRoot)
}

$coreProject = Join-Path $PclHostRoot "PCL.Core\PCL.Core.csproj"
if ([string]::IsNullOrWhiteSpace($PclCorePath)) {
    if (!(Test-Path -LiteralPath $coreProject)) {
        throw "PCL.Core project not found: $coreProject"
    }
    dotnet build $coreProject --configuration Release --property:Platform=AnyCPU
    if ($LASTEXITCODE -ne 0) { throw "PCL.Core build failed with exit code $LASTEXITCODE." }
    $PclCorePath = Join-Path $PclHostRoot "PCL.Core\bin\Release-AnyCPU\net8.0-windows\PCL.Core.dll"
}
$PclCorePath = [IO.Path]::GetFullPath($PclCorePath)
if (!(Test-Path -LiteralPath $PclCorePath)) {
    throw "PCL.Core.dll not found: $PclCorePath"
}

$project = Join-Path $repoRoot "src\PclNex.EasyTierLobby\PclNex.EasyTierLobby.csproj"
$smokeProject = Join-Path $repoRoot "tests\PclNex.EasyTierLobby.Smoke\PclNex.EasyTierLobby.Smoke.csproj"
$uiSmokeProject = Join-Path $repoRoot "tests\PclNex.EasyTierLobby.UiSmoke\PclNex.EasyTierLobby.UiSmoke.csproj"
$artifacts = Join-Path $repoRoot $OutputDir
$pluginDirectory = Join-Path $artifacts "pclnex.easytier"
$packagePath = Join-Path $artifacts "pclnex.easytier-$Version-anycpu.pclx"
$attributeVersion = if ($Version -match '^\d+\.\d+\.\d+$') { "$Version.0" } else { $Version }

$properties = @(
    "--property:Platform=AnyCPU",
    "--property:PluginVersion=$attributeVersion",
    "--property:PclHostRoot=$PclHostRoot",
    "--property:PclCorePath=$PclCorePath"
)

dotnet build $project -t:PublishPlugin --configuration $Configuration @properties
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed with exit code $LASTEXITCODE." }

dotnet run --project $smokeProject --configuration $Configuration @properties
if ($LASTEXITCODE -ne 0) { throw "Smoke test failed with exit code $LASTEXITCODE." }

dotnet run --project $uiSmokeProject --configuration $Configuration @properties
if ($LASTEXITCODE -ne 0) { throw "UI smoke test failed with exit code $LASTEXITCODE." }

$packagedPluginJsonPath = Join-Path $pluginDirectory "plugin.json"
$packagedPluginJson = Get-Content -LiteralPath $packagedPluginJsonPath -Raw | ConvertFrom-Json
$packagedPluginJson.version = $Version
$packagedPluginJsonContent = ($packagedPluginJson | ConvertTo-Json -Depth 20) + [Environment]::NewLine
[IO.File]::WriteAllText($packagedPluginJsonPath, $packagedPluginJsonContent, [Text.UTF8Encoding]::new($false))

$forbiddenFiles = @(
    "PCL.Core.dll",
    "Plain Craft Launcher 2.dll",
    "PCL.Plugin.Abstractions.dll",
    "Jint.dll",
    "Acornima.dll"
)
foreach ($forbiddenFile in $forbiddenFiles) {
    if (Get-ChildItem -LiteralPath $pluginDirectory -Recurse -File -Filter $forbiddenFile) {
        throw "Forbidden host/runtime assembly found in package: $forbiddenFile"
    }
}

$requiredFiles = @(
    "plugin.json",
    "README.md",
    "lib\PCL.EasyTierPlugin.dll",
    "mixins\pclnex.easytier.mixins.json"
)
foreach ($relativePath in $requiredFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $pluginDirectory $relativePath))) {
        throw "Required package file is missing: $relativePath"
    }
}

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}
[IO.Compression.ZipFile]::CreateFromDirectory($pluginDirectory, $packagePath)
$sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "Packed plugin to $packagePath"
Write-Host "SHA256: $sha256"
