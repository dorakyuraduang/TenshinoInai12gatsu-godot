# Rebuilding Original Video

This plugin derives from WMV Video 0.6.0. Its MIT notice is preserved in LICENSE. This fork adds MPEG playback, Android lifecycle handling and playback diagnostics.

## Dependencies

- FFmpeg n7.1.5, commit 3a0867c2bfda4a4d4309ca1a8cbdc6175e67f587 (unmodified). Extract ../../third_party/original-video-ffmpeg-7.1.5-source.zip and place the source directory containing configure at artifacts/video-build/deps/ffmpeg under the repository root.
- godot-cpp commit 27d9dd23c83871e0619fca5dc2cddfbfd69e926a from https://github.com/godotengine/godot-cpp, including its submodules, at artifacts/video-build/deps/godot-cpp.
- Python 3, SCons, MSYS2 with make and MinGW x64.
- For Android: NDK 28.1.13356709, API 24 minimum; native ELF segments use 16 KB alignment.

## Build

Run from the repository root, supplying your installed tool locations:

```powershell
./tools/build_video_plugin.ps1 -Platform windows -MsysRoot D:/msys2 -Scons C:/path/to/scons.exe
./tools/build_video_plugin.ps1 -Platform android -MsysRoot D:/msys2 -Scons C:/path/to/scons.exe -AndroidNdk D:/androidSdk/ndk/28.1.13356709
```

The script places temporary build files in artifacts/video-build and copies runtime libraries to addons/original_video/bin. Use -SkipFfmpeg for C++-only rebuilds and -Target debug or release for a single configuration. Do not run concurrent SCons builds against the same godot-cpp tree.

The exact FFmpeg configure recipes are tools/build_video_ffmpeg_windows_mingw.sh and tools/build_video_ffmpeg_android.sh. They enable shared libraries and selected ASF/MPEG demuxers and decoders; GPL and nonfree components are disabled. IO uses Godot FileAccess and FFmpeg AVIO callbacks. Original media is not bundled.

For a standalone plugin checkout, run SCons in the plugin directory with platform, target, arch, godot_cpp_dir and ffmpeg_root arguments, and copy runtime dependencies to the paths listed in original_video.gdextension. Keep the LGPL license, corresponding FFmpeg source and build recipes available with redistributed binaries. See THIRD_PARTY_NOTICES.md.

Windows editors may retain loaded DLLs until exit; restart the editor after replacing native libraries. Active Windows binaries are in bin/windows/x86_64; obsolete flat bin/*.dll files are excluded from this repository. This upload does not constitute a new device compatibility test.
