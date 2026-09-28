param(
    [ValidateSet('windows', 'android')][string]$Platform = 'android',
    [ValidateSet('debug', 'release', 'both')][string]$Target = 'both',
    [string]$MsysRoot = 'D:/msys2',
    [string]$AndroidNdk = 'D:/androidSdk/ndk/28.1.13356709',
    [string]$Scons = 'scons.exe',
    [int]$Jobs = 8,
    [switch]$SkipFfmpeg,
    [switch]$FfmpegOnly
)
$ErrorActionPreference = 'Stop'
$Scons = (Get-Command $Scons -ErrorAction Stop).Source
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'artifacts/video-build'
$plugin = Join-Path $root 'addons/original_video'
$deps = Join-Path $build 'deps'
$arch = if ($Platform -eq 'android') { 'arm64' } else { 'x86_64' }
$sdk = Join-Path $build "sdk/$Platform/$arch"
New-Item -ItemType Directory -Force -Path $build,$sdk,"$build/tmp" | Out-Null
function MsysPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path).Replace('\', '/')
    '/' + $full.Substring(0, 1).ToLowerInvariant() + $full.Substring(2)
}
function CopyRuntime([string]$source, [string]$destination) {
    $file = Join-Path $destination (Split-Path -Leaf $source)
    if ((Test-Path -LiteralPath $file) -and
        (Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $file).Hash) { return }
    Copy-Item -LiteralPath $source -Destination $destination -Force
}
foreach ($file in @("$deps/ffmpeg/configure", "$deps/godot-cpp/SConstruct", "$MsysRoot/usr/bin/bash.exe", $Scons)) {
    if (!(Test-Path -LiteralPath $file)) { throw "Build dependency is absent: $file. See addons/original_video/BUILDING.md." }
}
$vars = @{
    FFMPEG_SOURCE = MsysPath "$deps/ffmpeg"
    FFMPEG_BUILD = MsysPath "$build/ffmpeg-$Platform-$arch"
    FFMPEG_PREFIX = MsysPath $sdk
    MINGW_BIN = MsysPath "$MsysRoot/mingw64/bin"
    ANDROID_TOOLCHAIN = MsysPath "$AndroidNdk/toolchains/llvm/prebuilt/windows-x86_64"
    ANDROID_NDK_ROOT = $AndroidNdk
    ANDROID_API_LEVEL = '24'
    BUILD_JOBS = $Jobs.ToString()
    PATH = "$MsysRoot/mingw64/bin;$MsysRoot/usr/bin;$env:PATH"
    TMPDIR = MsysPath "$build/tmp"
}
$saved = @{}
try {
    foreach ($key in $vars.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $vars[$key], 'Process')
    }
    if (!$SkipFfmpeg) {
        $script = if ($Platform -eq 'android') { 'build_video_ffmpeg_android.sh' } else { 'build_video_ffmpeg_windows_mingw.sh' }
        $scriptPath = MsysPath (Join-Path $PSScriptRoot $script)
        $log = Join-Path $build "ffmpeg-$Platform.log"
        & "$MsysRoot/usr/bin/bash.exe" --noprofile --norc $scriptPath *> $log
        if ($LASTEXITCODE -ne 0) { throw "FFmpeg build failed: $log" }
    }
    if ($FfmpegOnly) { Write-Output "FFmpeg SDK ready: $sdk"; return }
    $targets = switch ($Target) {
        'debug' { @('template_debug') }
        'release' { @('template_release') }
        'both' { @('template_debug', 'template_release') }
    }
    foreach ($nativeTarget in $targets) {
        $args = @("platform=$Platform", "target=$nativeTarget", "arch=$arch",
            "godot_cpp_dir=$deps/godot-cpp", "ffmpeg_root=$sdk", "-j$Jobs", '-C', $plugin)
        if ($Platform -eq 'windows') { $args += @('use_mingw=yes', "mingw_prefix=$MsysRoot/mingw64") }
        else { $args += 'android_api_level=24' }
        $log = Join-Path $build "plugin-$Platform-$nativeTarget.log"
        & $Scons @args *> $log
        if ($LASTEXITCODE -ne 0) { throw "GDExtension build failed: $log" }
    }
    if ($Platform -eq 'android') {
        $dest = Join-Path $plugin 'bin/android/arm64-v8a'
        foreach ($lib in @('avcodec', 'avformat', 'avutil', 'swresample', 'swscale')) {
            CopyRuntime "$sdk/lib/lib$lib.so" $dest
        }
    } else {
        $dest = Join-Path $plugin 'bin/windows/x86_64'
        foreach ($lib in @('avcodec-61', 'avformat-61', 'avutil-59', 'swresample-5', 'swscale-8')) {
            CopyRuntime "$sdk/bin/$lib.dll" $dest
        }
        CopyRuntime "$MsysRoot/mingw64/bin/libwinpthread-1.dll" $dest
    }
    Write-Output "Original Video plugin built: $Platform $Target"
} finally {
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
}
