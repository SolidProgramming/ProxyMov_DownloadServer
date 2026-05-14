param(
    [string]$ProjectFile = "ProxyMov_DownloadServer/ProxyMov_DownloadServer/ProxyMov_DownloadServer.csproj",
    [string]$PublishProfile = "FolderProfile",
    [string]$PackageName = "DefaultAutoDLClient.zip",
    [string]$DeployDirectory = "A:\aniworld_webpanel\binaries",
    [switch]$SkipDeploy,
    [switch]$IncrementPatch,
    [switch]$IncrementMinor,
    [switch]$IncrementMajor
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction SilentlyContinue

    if ($null -ne $resolved) {
        return $resolved.Path
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

function Get-ProjectAssemblyVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CsprojPath
    )

    [xml]$projectXml = Get-Content -LiteralPath $CsprojPath
    $versionNode = $projectXml.Project.PropertyGroup.AssemblyVersion | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw "Keine <AssemblyVersion> im Projekt gefunden: $CsprojPath"
    }

    return $versionNode.Trim()
}

function Set-ProjectAssemblyVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CsprojPath,
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $projectXml = New-Object System.Xml.XmlDocument
    $projectXml.PreserveWhitespace = $true
    $projectXml.Load($CsprojPath)

    $propertyGroup = $projectXml.SelectSingleNode('/Project/PropertyGroup')

    if ($null -eq $propertyGroup) {
        throw "Keine PropertyGroup im Projekt gefunden: $CsprojPath"
    }

    $assemblyVersionNode = $propertyGroup.SelectSingleNode('AssemblyVersion')

    if ($null -eq $assemblyVersionNode) {
        $assemblyVersionNode = $projectXml.CreateElement('AssemblyVersion')
        [void]$propertyGroup.AppendChild($assemblyVersionNode)
    }

    $assemblyVersionNode.InnerText = $Version

    $fileVersionNode = $propertyGroup.SelectSingleNode('FileVersion')
    if ($null -ne $fileVersionNode) {
        $fileVersionNode.InnerText = $Version
    }

    $projectVersionNode = $propertyGroup.SelectSingleNode('Version')
    if ($null -ne $projectVersionNode) {
        $parsedVersion = [System.Version]::Parse($Version)
        $projectVersionNode.InnerText = '{0}.{1}.{2}' -f $parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build
    }

    $projectXml.Save($CsprojPath)
}

function Get-IncrementedPatchVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $parsedVersion = [System.Version]::Parse($Version.Trim())
    $revision = if ($parsedVersion.Revision -lt 0) { 0 } else { $parsedVersion.Revision }

    return '{0}.{1}.{2}.{3}' -f $parsedVersion.Major, $parsedVersion.Minor, ($parsedVersion.Build + 1), $revision
}

function Get-IncrementedMinorVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $parsedVersion = [System.Version]::Parse($Version.Trim())
    return '{0}.{1}.0.0' -f $parsedVersion.Major, ($parsedVersion.Minor + 1)
}

function Get-IncrementedMajorVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $parsedVersion = [System.Version]::Parse($Version.Trim())
    return '{0}.0.0.0' -f ($parsedVersion.Major + 1)
}

$projectPath = Resolve-FullPath -PathValue $ProjectFile
$projectDirectory = Split-Path -Parent $projectPath
$repoRoot = Resolve-FullPath -PathValue "."

$incrementSwitchCount = 0
if ($IncrementPatch) { $incrementSwitchCount++ }
if ($IncrementMinor) { $incrementSwitchCount++ }
if ($IncrementMajor) { $incrementSwitchCount++ }

if ($incrementSwitchCount -gt 1) {
    throw "Bitte nur einen Versionssprung angeben: -IncrementPatch, -IncrementMinor oder -IncrementMajor."
}

$assemblyVersion = Get-ProjectAssemblyVersion -CsprojPath $projectPath

if ($IncrementPatch) {
    $assemblyVersion = Get-IncrementedPatchVersion -Version $assemblyVersion
    Set-ProjectAssemblyVersion -CsprojPath $projectPath -Version $assemblyVersion
    Write-Host "Projektversion wurde auf $assemblyVersion erhoeht."
}
elseif ($IncrementMinor) {
    $assemblyVersion = Get-IncrementedMinorVersion -Version $assemblyVersion
    Set-ProjectAssemblyVersion -CsprojPath $projectPath -Version $assemblyVersion
    Write-Host "Projektversion wurde auf $assemblyVersion erhoeht."
}
elseif ($IncrementMajor) {
    $assemblyVersion = Get-IncrementedMajorVersion -Version $assemblyVersion
    Set-ProjectAssemblyVersion -CsprojPath $projectPath -Version $assemblyVersion
    Write-Host "Projektversion wurde auf $assemblyVersion erhoeht."
}

$artifactsRoot = Join-Path $repoRoot ".publish-artifacts"
$stagingDirectory = Join-Path $artifactsRoot "DefaultAutoDLClient"
$packagePath = Join-Path $artifactsRoot $PackageName
$deployPath = Join-Path $DeployDirectory $PackageName

$excludeDirectories = @(
    "appdata\Binaries\Chrome",
    "appdata\Binaries\ChromeHeadlessShell"
)

$excludeFiles = @(
    "*.pdb",
    "appsettings.Development.json"
)

Write-Host "Project: $projectPath"
Write-Host "Publish profile: $PublishProfile"
Write-Host "Staging directory: $stagingDirectory"
Write-Host "Package: $packagePath"

if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = Join-Path $artifactsRoot "nuget-packages"

$publishArguments = @(
    "publish"
    $projectPath
    "-m:1"
    "-p:PublishProfile=$PublishProfile"
    "-p:PublishDir=$stagingDirectory\"
    "-nologo"
    "-v:minimal"
)

Write-Host "Running dotnet publish..."
& dotnet @publishArguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

foreach ($relativeDirectory in $excludeDirectories) {
    $targetDirectory = Join-Path $stagingDirectory $relativeDirectory

    if (Test-Path -LiteralPath $targetDirectory) {
        Write-Host "Removing directory: $targetDirectory"
        Remove-Item -LiteralPath $targetDirectory -Recurse -Force
    }
}

foreach ($pattern in $excludeFiles) {
    Get-ChildItem -Path $stagingDirectory -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Host "Removing file: $($_.FullName)"
            Remove-Item -LiteralPath $_.FullName -Force
        }
}

Write-Host "Creating zip package..."
Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $packagePath -Force

if (-not $SkipDeploy) {
    if (-not (Test-Path -LiteralPath $DeployDirectory)) {
        throw "Deploy directory does not exist: $DeployDirectory"
    }

    Write-Host "Copying package to deploy directory..."
    Copy-Item -LiteralPath $packagePath -Destination $deployPath -Force
}

Write-Host "Done."
Write-Host "Staging directory: $stagingDirectory"
Write-Host "Zip package: $packagePath"

if (-not $SkipDeploy) {
    Write-Host "Deployed package: $deployPath"
}
