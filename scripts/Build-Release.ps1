param([string]$Iscc = 'ISCC.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (Test-Path artifacts/publish) { Remove-Item artifacts/publish -Recurse -Force }
    dotnet publish Folio.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $test = Start-Process (Join-Path $root 'artifacts/publish/Folio.exe') -ArgumentList '--self-test' -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw "Self-tests failed. See $env:TEMP\opencode\folio-selftest.txt" }
    & $Iscc (Join-Path $root 'installer/Folio.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    $version = ([xml](Get-Content Folio.csproj)).Project.PropertyGroup.Version
    $zip = Join-Path $root "artifacts/Folio-$version-win-x64-portable.zip"
    Compress-Archive -Path artifacts/publish/* -DestinationPath $zip -Force
    $files = @(Get-ChildItem artifacts/installer -Filter 'Folio-Setup-*.exe') + @(Get-Item $zip)
    $files | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } | Set-Content artifacts/SHA256SUMS.txt
} finally { Pop-Location }
