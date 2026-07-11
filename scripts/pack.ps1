param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.7",
    [string]$PclHostRoot = "",
    [string]$OutputDir = "artifacts"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\PclNex.EasyTierLobby\PclNex.EasyTierLobby.csproj"
$uiSmokeProject = Join-Path $repoRoot "tests\PclNex.EasyTierLobby.UiSmoke\PclNex.EasyTierLobby.UiSmoke.csproj"
$artifacts = Join-Path $repoRoot $OutputDir
$pluginDirectory = Join-Path $artifacts "pclnex.easytier"
$packagePath = Join-Path $artifacts "pclnex.easytier-v$Version.pclx"
$attributeVersion = if ($Version -match '^\d+\.\d+\.\d+$') { "$Version.0" } else { $Version }

$properties = @(
    "--property:Platform=AnyCPU",
    "--property:PluginVersion=$attributeVersion"
)
if (-not [string]::IsNullOrWhiteSpace($PclHostRoot)) {
    $properties += "--property:PclHostRoot=$PclHostRoot"
}

dotnet build $project -t:PublishPlugin --configuration $Configuration @properties
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed with exit code $LASTEXITCODE." }

dotnet run --project $uiSmokeProject --configuration $Configuration @properties
if ($LASTEXITCODE -ne 0) { throw "UI smoke test failed with exit code $LASTEXITCODE." }

$packagedPluginJsonPath = Join-Path $pluginDirectory "plugin.json"
$packagedPluginJson = Get-Content -LiteralPath $packagedPluginJsonPath -Raw | ConvertFrom-Json
$packagedPluginJson.version = $Version
$packagedPluginJsonContent = ($packagedPluginJson | ConvertTo-Json -Depth 20) + [Environment]::NewLine
[System.IO.File]::WriteAllText(
    $packagedPluginJsonPath,
    $packagedPluginJsonContent,
    [System.Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}
[System.IO.Compression.ZipFile]::CreateFromDirectory($pluginDirectory, $packagePath)
$sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "Packed plugin to $packagePath"
Write-Host "SHA256: $sha256"
