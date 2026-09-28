using System;
using System.IO;
using System.Linq;
using Godot;

namespace Tenshi;

internal static class GameResources
{
    internal static readonly string[] RequiredFiles =
        ["tenshi_dvd.a", "sys.a", "egbg.a", "char.a", "voice.a", "music.a", "se.a", "ed.a", "openning.v"];

    internal static string DirectoryFor(bool android, string projectDirectory, string sharedStorage = "")
    {
        if (!android) return Path.GetFullPath(Path.Combine(projectDirectory, "..", "天使不在的12月"));
        if (!sharedStorage.StartsWith('/') || sharedStorage == "/")
            throw new InvalidOperationException("无法取得手机内部共享存储目录。");
        // Use Android's path separators even when checking this policy on Windows.
        return sharedStorage.TrimEnd('/') + "/tenshi";
    }

    internal static string ResolveDirectory() => OS.HasFeature("android")
        ? DirectoryFor(true, "", AndroidStorage.SharedRoot())
        : DirectoryFor(false, ProjectSettings.GlobalizePath("res://"));

    internal static void ValidateReadable(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"未找到资源文件夹：\n{directory}\n\n请把原版资源包放在这个文件夹内。");
        string[] missing = RequiredFiles.Where(name => !File.Exists(Path.Combine(directory, name))).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException($"资源文件不完整：\n{directory}\n\n缺少：{string.Join("、", missing)}");
        foreach (string name in RequiredFiles)
        {
            // Read-only check; archives remain in place and are never unpacked.
            using FileStream file = File.OpenRead(Path.Combine(directory, name));
            if (file.Length == 0) throw new InvalidDataException($"资源文件为空：{name}");
        }
    }
}

internal static class AndroidStorage
{
    internal const string ManagePermission = "android.permission.MANAGE_EXTERNAL_STORAGE";
    internal const string ReadPermission = "android.permission.READ_EXTERNAL_STORAGE";

    internal static string PermissionFor(int apiLevel) => apiLevel >= 30 ? ManagePermission : ReadPermission;

    internal static string RequiredPermission()
    {
        using JavaClass version = JavaClassWrapper.Wrap("android.os.Build$VERSION");
        int sdk = version.Get("SDK_INT").AsInt32();
        CheckJavaException();
        if (sdk < 24) throw new InvalidOperationException("此版本需要 Android 7.0 或更高版本。");
        return PermissionFor(sdk);
    }

    internal static string SharedRoot()
    {
        using JavaClass environment = JavaClassWrapper.Wrap("android.os.Environment");
        using GodotObject? directory = environment.Call("getExternalStorageDirectory").AsGodotObject();
        CheckJavaException();
        if (directory == null) throw new InvalidOperationException("无法取得手机内部共享存储目录。");
        string path = directory.Call("getAbsolutePath").AsString();
        CheckJavaException();
        return path;
    }

    private static void CheckJavaException()
    {
        using JavaObject? error = JavaClassWrapper.GetException();
        if (error != null) throw new InvalidOperationException("Android 存储接口调用失败，请检查系统存储设置。");
    }
}
