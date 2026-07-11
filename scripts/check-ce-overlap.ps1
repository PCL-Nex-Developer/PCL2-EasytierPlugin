param(
    [string]$CeRoot = "",
    [string]$PluginSourceRoot = "",
    [double]$Threshold = 80
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CeRoot)) {
    $CeRoot = Join-Path $repoRoot "..\..\PCL-CE\Plain Craft Launcher 2"
}
if ([string]::IsNullOrWhiteSpace($PluginSourceRoot)) {
    $PluginSourceRoot = Join-Path $repoRoot "src\PclNex.EasyTierLobby"
}

$pairs = @(
    [pscustomobject]@{ Name = "Tools XAML"; Ce = "Pages\PageTools\PageToolsGameLink.xaml"; Plugin = "CeUi\PageTools\PageToolsGameLink.xaml" },
    [pscustomobject]@{ Name = "Tools CS"; Ce = "Pages\PageTools\PageToolsGameLink.xaml.cs"; Plugin = "CeUi\PageTools\PageToolsGameLink.xaml.cs" },
    [pscustomobject]@{ Name = "Setup XAML"; Ce = "Pages\PageSetup\PageSetupGameLink.xaml"; Plugin = "CeUi\PageSetup\PageSetupGameLink.xaml" },
    [pscustomobject]@{ Name = "Setup CS"; Ce = "Pages\PageSetup\PageSetupGameLink.xaml.cs"; Plugin = "CeUi\PageSetup\PageSetupGameLink.xaml.cs" }
)

$ceSourceFiles = @(
    "Modules\ModLink.cs",
    "Modules\Network\Http\Requester.cs",
    "Pages\PageTools\PageToolsGameLink.xaml",
    "Pages\PageTools\PageToolsGameLink.xaml.cs",
    "Pages\PageSetup\PageSetupGameLink.xaml",
    "Pages\PageSetup\PageSetupGameLink.xaml.cs"
)

$pluginSourceRoots = @(
    "CeOriginal",
    "CeLink",
    "CeCompat",
    "CeUi\PageTools",
    "CeUi\PageSetup"
)

function Get-NormalizedLines {
    param([string]$Path)

    Get-Content -LiteralPath $Path |
        ForEach-Object { ($_ -replace '\s+', ' ').Trim() } |
        Where-Object { $_.Length -gt 0 }
}

function Get-LineCounts {
    param([string[]]$Lines)

    $counts = @{}
    foreach ($line in $Lines) {
        if ($counts.ContainsKey($line)) {
            $counts[$line]++
        } else {
            $counts[$line] = 1
        }
    }
    $counts
}

function Compare-Lines {
    param(
        [string[]]$SourceLines,
        [string[]]$TargetLines
    )

    $targetCounts = Get-LineCounts $TargetLines
    $matchingLines = 0

    foreach ($line in $SourceLines) {
        if ($targetCounts.ContainsKey($line) -and $targetCounts[$line] -gt 0) {
            $matchingLines++
            $targetCounts[$line]--
        }
    }

    $denominator = [Math]::Max($SourceLines.Count, $TargetLines.Count)
    $similarity = if ($denominator -eq 0) { 100 } else { [Math]::Round(($matchingLines / [double]$denominator) * 100, 2) }
    $coverage = if ($SourceLines.Count -eq 0) { 100 } else { [Math]::Round(($matchingLines / [double]$SourceLines.Count) * 100, 2) }

    [pscustomobject]@{
        MatchingLines = $matchingLines
        Similarity = $similarity
        Coverage = $coverage
    }
}

$results = foreach ($pair in $pairs) {
    $cePath = Join-Path $CeRoot $pair.Ce
    $pluginPath = Join-Path $PluginSourceRoot $pair.Plugin

    if (!(Test-Path -LiteralPath $cePath)) {
        throw "Missing CE source file: $cePath"
    }
    if (!(Test-Path -LiteralPath $pluginPath)) {
        throw "Missing plugin source file: $pluginPath"
    }

    $ceLines = @(Get-NormalizedLines $cePath)
    $pluginLines = @(Get-NormalizedLines $pluginPath)
    $comparison = Compare-Lines $pluginLines $ceLines

    [pscustomobject]@{
        Name = $pair.Name
        CeLines = $ceLines.Count
        PluginLines = $pluginLines.Count
        MatchingLines = $comparison.MatchingLines
        Similarity = $comparison.Similarity
        Passed = $comparison.Similarity -ge $Threshold
        HashEqual = ((Get-FileHash -Algorithm SHA256 -LiteralPath $cePath).Hash -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $pluginPath).Hash)
    }
}

Write-Host "Per-file CE page overlap:"
$results | Format-Table -AutoSize

$cePaths = foreach ($relativePath in $ceSourceFiles) {
    $path = Join-Path $CeRoot $relativePath
    if (!(Test-Path -LiteralPath $path)) {
        throw "Missing CE source file: $path"
    }
    $path
}

$pluginPaths = foreach ($relativeRoot in $pluginSourceRoots) {
    $path = Join-Path $PluginSourceRoot $relativeRoot
    if (!(Test-Path -LiteralPath $path)) {
        throw "Missing plugin source root: $path"
    }
    Get-ChildItem -LiteralPath $path -Recurse -File -Include *.cs,*.xaml |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object { $_.FullName }
}

$allCeLines = foreach ($path in $cePaths) { Get-NormalizedLines $path }
$allPluginLines = foreach ($path in $pluginPaths) { Get-NormalizedLines $path }
$overall = Compare-Lines @($allCeLines) @($allPluginLines)

$perSourceFile = foreach ($path in $cePaths) {
    $lines = @(Get-NormalizedLines $path)
    $comparison = Compare-Lines $lines @($allPluginLines)
    [pscustomobject]@{
        File = $path.Substring($CeRoot.Length + 1)
        CeLines = $lines.Count
        MatchedLines = $comparison.MatchingLines
        Coverage = $comparison.Coverage
    }
}

Write-Host "`nCE source coverage inside plugin CE-port code:"
$perSourceFile | Format-Table -AutoSize

$overallResult = [pscustomobject]@{
    CeFiles = $cePaths.Count
    PluginFiles = @($pluginPaths).Count
    CeLines = @($allCeLines).Count
    PluginLines = @($allPluginLines).Count
    MatchingLines = $overall.MatchingLines
    Coverage = $overall.Coverage
    Passed = $overall.Coverage -ge $Threshold
}

Write-Host "`nOverall CE source coverage:"
$overallResult | Format-List

$failed = @($results | Where-Object { -not $_.Passed })
if ($failed.Count -gt 0) {
    throw "CE page overlap check failed: $($failed.Name -join ', ') below $Threshold%."
}

if (-not $overallResult.Passed) {
    throw "CE source coverage check failed: $($overallResult.Coverage)% below $Threshold%."
}

Write-Host "CE overlap check passed: page files and overall CE source coverage are >= $Threshold%."