# Build the Windows release: single-file ClearPower.exe, Inno Setup installer and a portable zip.
#   pwsh windows/build.ps1            -> windows/dist/ClearPower-Setup-<version>-x64.exe
#                                        windows/dist/ClearPower-<version>-x64-portable.zip
#                                        windows/dist/SHA256SUMS
#   pwsh windows/build.ps1 -SkipTests
# Needs the .NET SDK (8+) and Inno Setup 6 (ISCC.exe on PATH or in the usual install folders).
param([switch]$SkipTests)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$win = $PSScriptRoot
$version = (Get-Content (Join-Path $root "VERSION") -Raw).Trim()
$dist = Join-Path $win "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

Write-Host "== dotnet build (Release, $version)"
& dotnet build (Join-Path $win "ClearPower.sln") -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }
if (-not $SkipTests) {
  Write-Host "== dotnet test"
  & dotnet test (Join-Path $win "Tests\ClearPowerCoreTests\ClearPowerCoreTests.csproj") -c Release -p:Platform=x64 --no-build -nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

# Build outputs live outside the (OneDrive-synced) repo; see Directory.Build.props.
$bin = Join-Path $env:LOCALAPPDATA "ClearPower-build\ClearPower\bin\x64\Release\net48"
$exe = Join-Path $bin "ClearPower.exe"
if (-not (Test-Path $exe)) { throw "missing $exe" }

Write-Host "== stage"
$stage = Join-Path $env:LOCALAPPDATA "ClearPower-build\stage"
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $exe $stage
Copy-Item (Join-Path $root "LICENSE") $stage
Copy-Item (Join-Path $win "README.md") $stage

Write-Host "== installer"
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $iscc) {
  foreach ($c in @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $c) { $iscc = $c; break }
  }
}
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) not found" }
& $iscc /Q "/DAppVersion=$version" "/DStageDir=$stage" "/DOutDir=$dist" (Join-Path $win "installer\ClearPower.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host "== portable zip"
$zip = Join-Path $dist "ClearPower-$version-x64-portable.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip

Write-Host "== checksums"
$sums = @()
foreach ($f in Get-ChildItem $dist -File | Where-Object { $_.Name -ne "SHA256SUMS" }) {
  $h = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
  $sums += "$h  $($f.Name)"
}
Set-Content -Path (Join-Path $dist "SHA256SUMS") -Value ($sums -join "`n") -Encoding ascii
$sums | ForEach-Object { Write-Host $_ }
Write-Host "done: $dist"
