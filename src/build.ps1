# Build dist\ZapretVN.exe (single file: zapret binaries, fake packets and default lists are embedded).
# Requires only .NET Framework 4.8 (built into Windows 10/11). No Visual Studio needed.
#   powershell -ExecutionPolicy Bypass -File src\build.ps1          -> dist\ZapretVN.exe
#   powershell -ExecutionPolicy Bypass -File src\build.ps1 -Test    -> also dist\test\ZapretVN_test.exe (asInvoker, for --screenshot / --extract)
param([switch]$Test)
$ErrorActionPreference = 'Stop'

$src  = $PSScriptRoot
$root = Split-Path $src -Parent
$dist = Join-Path $root 'dist'
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc (.NET Framework 4.8 required)" }

# csc fails on long/unicode temp paths, so build in a short ASCII folder
$b = Join-Path $env:TEMP 'zvn-build'
if (Test-Path $b) { Remove-Item $b -Recurse -Force }
New-Item -ItemType Directory -Force $b | Out-Null
Copy-Item "$src\ZapretVN.cs", "$src\Shield.cs", "$src\app.manifest", "$src\app.ico" $b

# payload: bin/, fake/, lists/ -> embedded resources "payload/<dir>/<file>"
$res = @()
foreach ($dir in 'bin', 'fake', 'lists') {
    New-Item -ItemType Directory -Force "$b\payload\$dir" | Out-Null
    foreach ($f in Get-ChildItem (Join-Path $root $dir) -File) {
        Copy-Item $f.FullName "$b\payload\$dir\"
        $res += "/resource:payload\$dir\$($f.Name),payload/$dir/$($f.Name)"
    }
}
if (-not ($res -match 'payload/bin/winws.exe')) { throw 'bin\winws.exe is missing' }

$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.ServiceProcess.dll', '/r:Microsoft.CSharp.dll'
$common = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/win32icon:app.ico') + $refs + $res

Push-Location $b
try {
    & $csc @common /win32manifest:app.manifest /out:ZapretVN.exe ZapretVN.cs Shield.cs
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    New-Item -ItemType Directory -Force $dist | Out-Null
    Copy-Item ZapretVN.exe $dist -Force

    if ($Test) {
        (Get-Content app.manifest) -replace 'requireAdministrator', 'asInvoker' | Set-Content test.manifest
        & $csc @common /win32manifest:test.manifest /out:ZapretVN_test.exe ZapretVN.cs Shield.cs
        if ($LASTEXITCODE -ne 0) { throw "test build failed ($LASTEXITCODE)" }
        New-Item -ItemType Directory -Force "$dist\test" | Out-Null
        Copy-Item ZapretVN_test.exe "$dist\test\" -Force
    }
}
finally { Pop-Location }

$exe = Get-Item "$dist\ZapretVN.exe"
$sha = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash
Write-Host ("OK: dist\ZapretVN.exe  {0:N0} bytes  SHA256 {1}" -f $exe.Length, $sha)
