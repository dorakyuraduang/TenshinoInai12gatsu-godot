using System;
using System.IO;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private string _gameDirectory = "";
    private bool _resourcesReady, _resourceLoadStarted, _checkingStartup;
    private string _storagePermission = "";
    private Control? _startupPanel;
    private Label _startupText = null!;
    private Button _authorizeStorage = null!;

    private void StartPlatformResources()
    {
        BuildStartupPanel();
        GetTree().OnRequestPermissionsResult += OnStoragePermissionResult;
        TryStartResources();
    }

    private void BuildStartupPanel()
    {
        _startupPanel = new Control { MouseFilter = MouseFilterEnum.Stop };
        _startupPanel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_startupPanel);
        var ground = new ColorRect { Color = new Color(0.045f, 0.065f, 0.09f), MouseFilter = MouseFilterEnum.Ignore };
        ground.SetAnchorsPreset(LayoutPreset.FullRect);
        _startupPanel.AddChild(ground);
        _startupText = new Label { Position = new Vector2(48, 55), Size = new Vector2(704, 365),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Center };
        _startupText.AddThemeFontOverride("font", _uiFont);
        _startupText.AddThemeFontSizeOverride("font_size", 24);
        _startupPanel.AddChild(_startupText);
        _authorizeStorage = StartupButton("允许读取资源", 48, RequestStorageAccess);
        StartupButton("重新检查", 294, () =>
        {
            if (_resourceLoadStarted) GetTree().ReloadCurrentScene();
            else TryStartResources();
        });
        StartupButton("退出", 540, () => GetTree().Quit());
    }

    private Button StartupButton(string text, float x, Action pressed)
    {
        var button = new Button { Text = text, Position = new Vector2(x, 456), Size = new Vector2(212, 64) };
        button.AddThemeFontOverride("font", _uiFont);
        button.AddThemeFontSizeOverride("font_size", 23);
        button.Pressed += pressed;
        _startupPanel!.AddChild(button);
        return button;
    }

    private void TryStartResources()
    {
        if (_checkingStartup || _resourcesReady || _resourceLoadStarted) return;
        _checkingStartup = true;
        try
        {
            bool android = OS.HasFeature("android");
            _gameDirectory = GameResources.ResolveDirectory();
            _storagePermission = android ? AndroidStorage.RequiredPermission() : "";
            if (android && !OS.GetGrantedPermissions().Contains(_storagePermission))
            {
                ShowStartupIssue($"游戏从内部存储的 tenshi 文件夹读取原版资源：\n{_gameDirectory}\n\n" +
                    (_storagePermission == AndroidStorage.ManagePermission
                        ? "请点击“允许读取资源”，在系统页面开启“允许管理所有文件”，然后返回游戏。"
                        : "请点击“允许读取资源”，授予存储读取权限。"), true);
                return;
            }
            GameResources.ValidateReadable(_gameDirectory);
            _resourceLoadStarted = true;
            InitializeGameResources();
            if (_resourcesReady) _startupPanel!.Hide();
        }
        catch (Exception error)
        {
            ShowStartupIssue(error.Message, OS.HasFeature("android"));
            if (OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--smoke-") || arg == "--validate-archives"))
            { GD.PushError(error.ToString()); GetTree().Quit(1); }
        }
        finally { _checkingStartup = false; }
    }

    private void ShowStartupIssue(string message, bool allowPermission = false)
    {
        _startupText.Text = message;
        _authorizeStorage.Visible = allowPermission && _storagePermission.Length > 0;
        _startupPanel!.Show();
        _menu.Hide();
        _game.Hide();
        _splash.Hide();
    }

    private void RequestStorageAccess()
    {
        if (!OS.HasFeature("android") || _storagePermission.Length == 0) return;
        // Godot 4.5 handles the special Android 11+ Settings intent, with a
        // fallback to the general all-files settings screen. Recheck on resume.
        if (OS.RequestPermission(_storagePermission)) TryStartResources();
    }

    private void OnStoragePermissionResult(string permission, bool granted)
    {
        if (permission != _storagePermission || _resourcesReady) return;
        Callable.From(TryStartResources).CallDeferred();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest)
        {
            Callable.From(HandleAndroidBackRequest).CallDeferred();
            return;
        }
        if ((what == NotificationApplicationResumed || what == NotificationApplicationFocusIn) && OS.HasFeature("android") &&
            _startupPanel != null && !_resourcesReady)
            Callable.From(TryStartResources).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (_startupPanel != null) GetTree().OnRequestPermissionsResult -= OnStoragePermissionResult;
    }

    private void SmokePlatformResources()
    {
        try
        {
            if (GameResources.DirectoryFor(true, "ignored", "/storage/emulated/0") != "/storage/emulated/0/tenshi" ||
                GameResources.DirectoryFor(true, "ignored", "/storage/emulated/10/") != "/storage/emulated/10/tenshi" ||
                AndroidStorage.PermissionFor(29) != AndroidStorage.ReadPermission ||
                AndroidStorage.PermissionFor(30) != AndroidStorage.ManagePermission)
                throw new InvalidDataException("Android storage path or permission policy differs");
            string directory = Path.Combine(Path.GetDirectoryName(DataPath("probe"))!, "resource-check-" + Guid.NewGuid().ToString("N"));
            bool missing = false;
            try { GameResources.ValidateReadable(directory); } catch (DirectoryNotFoundException) { missing = true; }
            if (!missing) throw new InvalidDataException("Missing resource directory was accepted");
            Directory.CreateDirectory(directory);
            foreach (string name in GameResources.RequiredFiles) File.WriteAllBytes(Path.Combine(directory, name), [1]);
            // Synthetic one-byte files exercise the startup policy, not media decoding.
            GameResources.ValidateReadable(directory);
            File.WriteAllBytes(Path.Combine(directory, "ed.a"), []);
            bool empty = false;
            try { GameResources.ValidateReadable(directory); } catch (InvalidDataException) { empty = true; }
            if (!empty) throw new InvalidDataException("Empty ending archive was accepted");
            File.Delete(Path.Combine(directory, "openning.v"));
            bool absent = false;
            try { GameResources.ValidateReadable(directory); }
            catch (FileNotFoundException error) { absent = error.Message.Contains("openning.v"); }
            if (!absent) throw new InvalidDataException("Missing OP was not named in the startup error");
            GD.Print("Platform storage checks passed: Android shared root (including secondary user), API 29/30 permissions, missing directory/files, empty archive, complete file set");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
