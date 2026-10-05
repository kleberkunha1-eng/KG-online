[CmdletBinding()]
param(
    [string]$ClientRoot = 'E:\NEW SV\Client',
    [switch]$SkipUnityImport,
    [switch]$RawOnly,
    [switch]$ModelsOnly,
    [switch]$SceneOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ClientRoot = (Resolve-Path $ClientRoot).Path
$projectVersionFile = Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
$versionLine = Get-Content $projectVersionFile | Where-Object { $_ -like 'm_EditorVersion:*' } | Select-Object -First 1
$unityVersion = $versionLine.Split(':', 2)[1].Trim()
$unityRoot = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion"
$unity = Join-Path $unityRoot 'Editor\Unity.exe'
$frameworkPath = Join-Path $unityRoot 'Editor\Data\MonoBleedingEdge\lib\mono\4.8-api'
$msbuild = Get-ChildItem (Join-Path $env:ProgramFiles 'Microsoft Visual Studio') -Recurse -Filter MSBuild.exe -ErrorAction Stop |
    Where-Object { $_.FullName -match '\\MSBuild\\Current\\Bin\\MSBuild\.exe$' } |
    Select-Object -First 1 -ExpandProperty FullName
$converterProject = Join-Path $PSScriptRoot 'PKO-file-viewer\BatchExporter\BatchExporter.csproj'
$converterOutput = Join-Path $PSScriptRoot 'PKO-file-viewer\BatchExporter\bin\Release-Fixed'
$converter = Join-Path $converterOutput 'PKOAssetBatchExporter.exe'
$outputRoot = Join-Path $projectRoot 'Assets\PKO_Data\ClientImport'
$stagingRoot = Join-Path $env:TEMP "PKOClientImport-$PID"

function Copy-MissingFiles([string]$SourceDirectory, [string]$TargetDirectory, [switch]$UpdateChanged) {
    if (-not (Test-Path $SourceDirectory)) { return }
    $sourcePrefix = (Resolve-Path $SourceDirectory).Path.TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem $SourceDirectory -Recurse -File) {
        $relativePath = $file.FullName.Substring($sourcePrefix.Length)
        $destination = Join-Path $TargetDirectory $relativePath
        if (Test-Path $destination) {
            if (-not $UpdateChanged -or (Get-Item $destination).LastWriteTimeUtc -ge $file.LastWriteTimeUtc) { continue }
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}

function Remove-StaleObjStubs([string]$CanonicalModels, [string]$MirrorModels) {
    $canonicalPrefix = (Resolve-Path $CanonicalModels).Path.TrimEnd('\') + '\'
    $mirrorPrefix = (Resolve-Path $MirrorModels).Path.TrimEnd('\') + '\'
    foreach ($obj in Get-ChildItem $MirrorModels -Recurse -File -Filter '*.obj') {
        $relativePath = $obj.FullName.Substring($mirrorPrefix.Length)
        if (Test-Path (Join-Path $CanonicalModels $relativePath)) { continue }
        if (Select-String -LiteralPath $obj.FullName -Pattern '^f\s' -Quiet) { continue }

        $mtlPath = [IO.Path]::ChangeExtension($obj.FullName, '.mtl')
        foreach ($path in @($obj.FullName, ($obj.FullName + '.meta'), $mtlPath, ($mtlPath + '.meta'))) {
            if (Test-Path $path) { Remove-Item -LiteralPath $path -Force }
        }
    }
}

if (-not (Test-Path $unity)) { throw "Unity $unityVersion not found at $unity" }
if (-not (Test-Path $frameworkPath)) { throw "Unity .NET reference assemblies not found at $frameworkPath" }
if (-not $msbuild) { throw 'MSBuild was not found in Visual Studio installations.' }
$incrementalModes = @($RawOnly, $ModelsOnly, $SceneOnly) | Where-Object { $_ }
if ($incrementalModes.Count -gt 1) { throw 'Choose only one incremental import mode.' }

$staleExporter = Get-Process -Name PKOAssetBatchExporter -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -like '*\PKOAssetBatchExporter.exe' -or $_.Path -eq $converter
}
if ($staleExporter) {
    foreach ($process in $staleExporter) {
        Write-Output "Stopping stale PKO converter PID $($process.Id) from $($process.Path)"
        Stop-Process -Id $process.Id -Force
    }
}

$runningUnity = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $unity }
if (($RawOnly -or $ModelsOnly -or $SceneOnly) -and -not (Test-Path $outputRoot)) { throw 'Incremental import requires existing client assets.' }
if (-not ($RawOnly -or $ModelsOnly -or $SceneOnly) -and (Test-Path $outputRoot)) { throw "Import destination already exists: $outputRoot" }
if (-not ($ModelsOnly -or $SceneOnly) -and (Test-Path $stagingRoot)) { throw "Staging destination already exists: $stagingRoot" }
if (Test-Path $converterOutput) { Remove-Item -LiteralPath $converterOutput -Recurse -Force }

$env:OutDir = $converterOutput + '\'
$env:FrameworkPathOverride = $frameworkPath
& $msbuild $converterProject /t:Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Could not build the PKO asset converter.' }

if ($SceneOnly) {
    & $converter $ClientRoot $outputRoot --scene-models
    if ($LASTEXITCODE -ne 0) { throw 'The PKO scene-model conversion failed. Review the messages above.' }
    Copy-MissingFiles (Join-Path $outputRoot 'Models') (Join-Path $projectRoot 'Assets\ImportedClient\Models') -UpdateChanged
    Copy-MissingFiles (Join-Path $outputRoot 'Textures') (Join-Path $projectRoot 'Assets\ImportedClient\Textures') -UpdateChanged
    Remove-StaleObjStubs (Join-Path $outputRoot 'Models') (Join-Path $projectRoot 'Assets\ImportedClient\Models')
}
elseif ($ModelsOnly) {
    & $converter $ClientRoot $outputRoot --missing-models
    if ($LASTEXITCODE -ne 0) { throw 'The PKO missing-model conversion failed. Review the messages above.' }
    Copy-MissingFiles (Join-Path $outputRoot 'Models') (Join-Path $projectRoot 'Assets\ImportedClient\Models') -UpdateChanged
    Copy-MissingFiles (Join-Path $outputRoot 'Textures') (Join-Path $projectRoot 'Assets\ImportedClient\Textures') -UpdateChanged
    Remove-StaleObjStubs (Join-Path $outputRoot 'Models') (Join-Path $projectRoot 'Assets\ImportedClient\Models')
}
elseif ($RawOnly) {
    & $converter $ClientRoot $stagingRoot --raw
    if ($LASTEXITCODE -ne 0) { throw 'The PKO raw-data import failed. Review the messages above.' }

    foreach ($targetRoot in @($outputRoot, (Join-Path $projectRoot 'Assets\ImportedClient'))) {
        foreach ($folder in @('Source', 'Audio')) {
            Copy-MissingFiles (Join-Path $stagingRoot $folder) (Join-Path $targetRoot $folder)
        }
    }
}
else {
    & $converter $ClientRoot $stagingRoot --all
    if ($LASTEXITCODE -ne 0) { throw 'The PKO asset conversion completed with errors. Review the messages above.' }

    Move-Item -LiteralPath $stagingRoot -Destination $outputRoot
}

if (-not $SkipUnityImport -and -not $runningUnity) {
    $logPath = Join-Path $projectRoot 'Tools\pko-unity-import.log'
    & $unity -batchmode -nographics -quit -projectPath $projectRoot -logFile $logPath
    if ($LASTEXITCODE -ne 0) { throw "Unity import failed. Review $logPath" }
}

if ($SceneOnly) { Write-Output "PKO scene models and textures reconverted and mirrored: $outputRoot" }
elseif ($ModelsOnly) { Write-Output "Missing PKO models converted and mirrored: $outputRoot" }
elseif ($RawOnly) { Write-Output "PKO raw client data merged: $outputRoot" }
else { Write-Output "PKO client import finished: $outputRoot" }
if ($runningUnity) { Write-Output 'Unity Editor is already open and will refresh the imported assets.' }