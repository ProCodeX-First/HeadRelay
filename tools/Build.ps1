# SPDX-License-Identifier: MIT
[CmdletBinding()]
param([string]$ZigPath = 'zig')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$zig = (Get-Command $ZigPath -CommandType Application -ErrorAction Stop).Source
$compiler = Split-Path -Parent $zig
$sources = Join-Path $root 'native'
$artifacts = Join-Path $root 'artifacts'
$work = Join-Path $artifacts ([guid]::NewGuid().ToString('N'))
$bridge = Join-Path $work 'bridge'
$stage = Join-Path $work 'package'
$probe = Join-Path $work 'x86-test'
New-Item -ItemType Directory -Force -Path $bridge, $stage, $probe | Out-Null

function Invoke-Zig([string[]]$Arguments) {
    & $zig @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Zig failed ($LASTEXITCODE): $($Arguments[0])" }
}

function Compile-Object($Target, $InputName, $Object) {
    Invoke-Zig @('clang', '-target', $Target, '-O2', '-fno-stack-protector', '-fno-builtin',
        '-isystem', "$compiler/lib/include", '-isystem', "$compiler/lib/libc/include/any-windows-any",
        '-c', "$sources/$InputName", '-o', $Object)
}

function Import-Library($Machine, $Definition, $Output, [switch]$KillAt) {
    $arguments = @('dlltool', '-m', $Machine, '-d', "$sources/$Definition", '-l', $Output)
    if ($KillAt) { $arguments += '-k' }
    Invoke-Zig $arguments
}

foreach ($bits in 32, 64) {
    if ($bits -eq 32) {
        $target = 'i686-windows-gnu'
        $machine = 'i386'
        $arch = 'x86'
        $kernel = 'kernel32-x86.def'
        $exports = 'AxisBridge32.def'
        $name = 'NPClient.dll'
        $ftName = 'freetrackclient.dll'
        $entry = 'DllMain@12'
    } else {
        $target = 'x86_64-windows-gnu'
        $machine = 'i386:x86-64'
        $arch = 'x64'
        $kernel = 'kernel32.def'
        $exports = 'AxisBridge.def'
        $name = 'NPClient64.dll'
        $ftName = 'freetrackclient64.dll'
        $entry = 'DllMain'
    }
    Compile-Object $target 'AxisBridge.c' "$work/bridge$bits.obj"
    Import-Library $machine $kernel "$work/kernel$bits.lib" -KillAt:($bits -eq 32)
    Import-Library $machine 'msvcrt.def' "$work/crt$bits.lib"
    Invoke-Zig @('lld-link', '/dll', '/nodefaultlib', "/entry:$entry", '/subsystem:windows',
        "/machine:$arch", '/dynamicbase', '/nxcompat', '/timestamp:0', "/out:$bridge/$name",
        "/def:$sources/$exports", "$work/bridge$bits.obj", "$work/kernel$bits.lib", "$work/crt$bits.lib")
    Copy-Item -LiteralPath "$bridge/$name" -Destination "$bridge/$ftName"
}

Import-Library 'i386:x86-64' 'shell32.def' "$work/shell64.lib"
Compile-Object 'x86_64-windows-gnu' 'Presence.c' "$work/presence.obj"
Invoke-Zig @('lld-link', '/nodefaultlib', '/entry:WinMain', '/subsystem:windows', '/machine:x64',
    '/dynamicbase', '/nxcompat', '/timestamp:0', "/out:$bridge/TrackIR.exe", "$work/presence.obj",
    "$work/kernel64.lib", "$work/shell64.lib")

& dotnet publish "$root/src/HeadRelay.csproj" -c Release "-p:BridgeDirectory=$bridge" --self-contained false -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Managed publish failed' }

# Package the fresh publish directory before a test run can create logs or local state.
$zipName = 'HeadRelay-0.2.0-Windows.zip'
$archive = Join-Path $work $zipName
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $archive)
$test = Join-Path $work 'unpacked'
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $test)
$selfTest = Start-Process -FilePath "$test/HeadRelay.exe" -ArgumentList '--test' -WorkingDirectory $test -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath "$test/Tests/TestResults.txt"
if ($selfTest.ExitCode -ne 0) { throw "HeadRelay self-test failed ($($selfTest.ExitCode))" }

Import-Library 'i386' 'kernel-test32.def' "$work/probe-kernel.lib" -KillAt
Compile-Object 'i686-windows-gnu' 'BridgeTest32.c' "$work/probe.obj"
Invoke-Zig @('lld-link', '/nodefaultlib', '/entry:AxisTest', '/subsystem:console', '/machine:x86',
    "/out:$probe/BridgeTest32.exe", "$work/probe.obj", "$work/probe-kernel.lib", "$work/crt32.lib")
Copy-Item -LiteralPath "$test/Bridge/NPClient.dll" -Destination "$probe/NPClient.dll"
& "$probe/BridgeTest32.exe"
if ($LASTEXITCODE -ne 0) { throw 'Native x86 bridge test failed' }

Copy-Item -LiteralPath $archive -Destination (Join-Path $artifacts $zipName) -Force
Write-Output "Release: artifacts/$zipName"
