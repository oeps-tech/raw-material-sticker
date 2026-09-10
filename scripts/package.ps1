param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.5',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
function New-PortableZip([string]$Directory, [string]$ZipPath) {
    $sourceRoot = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse) {
            if ($file.Extension -eq '.pdb') { continue }
            $entryName = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $zip.Dispose() }
}
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    if ($Dotnet -eq 'dotnet' -and (Test-Path -LiteralPath '.tools\dotnet\dotnet.exe')) { $Dotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe' }
    $outputRoot = Join-Path $repoRoot 'artifacts'
    $stage = Join-Path $outputRoot ('package-' + [Guid]::NewGuid().ToString('N'))
    $appStage = Join-Path $stage 'app-package\app'
    $fullStage = Join-Path $stage 'full-installer'
    New-Item -ItemType Directory -Force -Path $appStage, $fullStage | Out-Null
    & $Dotnet build Oeps.RawMaterialSticker.sln -c Release -p:Version=$Version --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $Dotnet run --project tests/Oeps.RawMaterialSticker.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
    & $Dotnet publish src/Oeps.RawMaterialSticker.App -c Release -r win-x64 --self-contained false -p:Version=$Version -p:CopyOutputSymbolsToPublishDirectory=false -o $appStage --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
    & $Dotnet publish src/Oeps.RawMaterialSticker.Launcher -c Release -r win-x64 --self-contained false -p:Version=$Version -p:CopyOutputSymbolsToPublishDirectory=false -p:AppHostRelativeDotNet=../runtime -o (Join-Path $fullStage 'launcher') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
    $manifest = @{ version=$Version; runtimeMajor=10; architecture='x64'; executable='Oeps.RawMaterialSticker.App.exe' } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $appStage 'update-manifest.json'), $manifest, [Text.UTF8Encoding]::new($false))
    $packageName = "Oeps.RawMaterialSticker-$Version-win-x64.zip"
    $packagePath = Join-Path $outputRoot $packageName
    if (Test-Path -LiteralPath $packagePath) { throw "Package already exists: $packagePath. Use a new version or archive the existing artifact first." }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-PortableZip (Split-Path -Parent $appStage) $packagePath
    $checksum = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $packageName
    [IO.File]::WriteAllText(($packagePath + '.sha256'), ($checksum + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $packagePath, ($packagePath + '.sha256') -Destination $fullStage
    $installerName = "Oeps.RawMaterialSticker-$Version-setup-win-x64.msi"
    $installerPath = Join-Path $outputRoot $installerName
    & (Join-Path $PSScriptRoot 'Build-Msi.ps1') -Stage $fullStage -Version $Version -Output $installerPath -Dotnet $Dotnet
    $installerChecksum = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $installerName
    [IO.File]::WriteAllText(($installerPath + '.sha256'), ($installerChecksum + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Write-Output "App package: $packagePath"
    Write-Output "Initial installer: $installerPath"
    Write-Output "Build staging retained for inspection: $stage"
}
finally { Pop-Location }
