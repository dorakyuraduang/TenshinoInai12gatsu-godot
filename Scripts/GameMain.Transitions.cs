using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private TextureRect _sceneCover = null!;
    private ImageTexture? _sceneSnapshot;
    private double _sceneFadeDuration;
    private int _sceneEffect, _sceneStep;
    private readonly Dictionary<string, ImageTexture> _wipeMasks = new();
    private ShaderMaterial? _transitionMaterial;
    private bool SkipEffects => !_preferences.Effects || _skipRunning || Input.IsKeyPressed(Key.Ctrl) ||
        OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--smoke-") && a != "--smoke-effects");

    private void CaptureSceneTransition()
    {
        if (_sceneSnapshot != null || SkipEffects || DisplayServer.GetName() == "headless") return;
        bool needed = _events.Skip(_eventIndex + 1).TakeWhile(e => e.Kind != SceneEventKind.Dialogue)
            .Any(e => e.Kind is SceneEventKind.Transition or SceneEventKind.MessageLayout or SceneEventKind.ScreenEffect);
        if (!needed) return;
        using Image image = GetViewport().GetTexture().GetImage();
        if (!image.IsEmpty()) _sceneSnapshot = ImageTexture.CreateFromImage(image);
    }

    private bool BeginSceneTransition(int effect, int step = 0)
    {
        if (effect is 40 or 42 or 43)
        {
            SetSceneTone(effect == 40 ? 0 : effect);
            if (step != 0) { EndSceneTransition(); return false; }
            effect = 2;
        }
        if (SkipEffects) { EndSceneTransition(); return false; }

        // The original dispatcher routes 12, 41 and 50 through its scene
        // fade envelope before their effect-specific draw kernel runs.  The
        // kernels are still being mapped from IDA; keeping the verified
        // 127 -> 0 timing here prevents these script commands from being
        // silently skipped while retaining the original scene ordering.
        if (effect is 12 or 41 or 50)
        {
            _sceneEffect = effect;
            _sceneStep = effect == 12 ? 4 : 2;
            _sceneFadeDuration = Math.Ceiling(128.0 / _sceneStep) / 60;
            _scriptPause = _sceneFadeDuration;
            PrepareSceneCover(0);
            UpdateSceneTransition();
            return true;
        }

        string? wipe = WipeName(effect);
        if (wipe != null && !_uiArchive!.Contains(wipe))
        { GD.PushWarning($"Original unused transition resource is absent: {wipe}"); EndSceneTransition(); return false; }
        if (effect is not (2 or 3) && wipe == null) { EndSceneTransition(); return false; }
        _sceneEffect = effect;
        _sceneStep = wipe != null ? effect is 34 or 35 ? 8 : effect is 36 or 37 ? 2 : 4 :
            effect == 3 ? 8 : step > 0 ? step : 2;
        _sceneFadeDuration = Math.Ceiling((wipe == null ? 128.0 : 256.0) / _sceneStep) / 60;
        _scriptPause = _sceneFadeDuration;
        PrepareSceneCover(wipe == null ? 0 : 1);
        if (wipe != null) TransitionMaterial().SetShaderParameter("wipe_mask", LoadWipeMask(wipe));
        UpdateSceneTransition();
        return true;
    }

    private void UpdateSceneTransition()
    {
        if (_sceneFadeDuration <= 0) return;
        int frame = Math.Max(0, (int)Math.Floor((_sceneFadeDuration - _scriptPause) * 60 + 0.00001));
        ShaderMaterial material = TransitionMaterial();
        if (_sceneEffect >= 4959)
        {
            var sample = OriginalEffectMath.Sample(_sceneEffect, frame);
            material.SetShaderParameter("shift", sample.Shift / new Vector2(800, 600));
            material.SetShaderParameter("zoom", sample.Zoom);
            material.SetShaderParameter("flash", sample.Flash);
        }
        else material.SetShaderParameter("amount", WipeName(_sceneEffect) != null ?
            (float)Math.Min(255, frame * _sceneStep) : Math.Max(0, 127 - frame * _sceneStep) / 128f);
    }

    private void EndSceneTransition()
    {
        _sceneFadeDuration = 0;
        _sceneEffect = 0;
        _sceneCover.Visible = false;
        _sceneCover.Texture = null;
        _sceneCover.Material = null;
        _sceneSnapshot?.Dispose();
        _sceneSnapshot = null;
    }

    private ImageTexture LoadWipeMask(string name)
    {
        if (_wipeMasks.TryGetValue(name, out ImageTexture? texture)) return texture;
        var entry = _uiArchive!.Read(name);
        using Image image = PxDecoder.DecodeBackground(entry.Data, entry.Kind);
        if (image.GetWidth() != 800 || image.GetHeight() != 600)
            throw new InvalidOperationException($"Unexpected original wipe dimensions: {name}");
        _wipeMasks[name] = texture = ImageTexture.CreateFromImage(image);
        return texture;
    }

    private static string? WipeName(int effect) => effect switch
    {
        30 or 34 => "wipe01.px", 31 or 35 => "wipe01R.px", 32 => "wipe02R.px",
        33 => "wipe02.px", 36 => "wave00.px", 37 => "wave01.px", _ => null
    };

    private ShaderMaterial TransitionMaterial()
    {
        if (_transitionMaterial != null) return _transitionMaterial;
        var shader = new Shader { Code = """
            shader_type canvas_item;
            uniform int mode = 0;
            uniform float amount = 1.0;
            uniform sampler2D wipe_mask : filter_nearest, repeat_disable;
            uniform vec2 shift = vec2(0.0);
            uniform float zoom = 1.0;
            uniform float flash = 0.0;
            void fragment() {
                vec2 sample_uv = (UV - vec2(0.49875, 0.498333) - shift) / zoom + vec2(0.49875, 0.498333);
                vec4 pixel = texture(TEXTURE, sample_uv);
                if (mode == 1) {
                    // IDA 10032AB0 clamps the mask difference to an eight-level edge.
                    pixel.a *= clamp((texture(wipe_mask, UV).r * 255.0 - amount) / 8.0, 0.0, 1.0);
                } else if (mode == 2) {
                    pixel.rgb = mix(pixel.rgb, vec3(1.0), flash);
                    if (any(lessThan(sample_uv, vec2(0.0))) || any(greaterThanEqual(sample_uv, vec2(1.0))))
                        pixel.rgb = vec3(0.0);
                    pixel.a = 1.0;
                } else {
                    pixel.a *= amount;
                }
                COLOR = pixel;
            }
            """ };
        return _transitionMaterial = new ShaderMaterial { Shader = shader };
    }

    private void PrepareSceneCover(int mode)
    {
        ShaderMaterial material = TransitionMaterial();
        material.SetShaderParameter("mode", mode);
        material.SetShaderParameter("shift", Vector2.Zero);
        material.SetShaderParameter("zoom", 1f);
        material.SetShaderParameter("flash", 0f);
        _sceneCover.Material = material;
        _sceneCover.Texture = _sceneSnapshot;
        _sceneCover.Modulate = Colors.White;
        _sceneCover.Visible = _sceneSnapshot != null;
    }

    private bool BeginScreenEffect(int effect)
    {
        if (SkipEffects) { EndSceneTransition(); return false; }
        _sceneEffect = effect;
        _sceneFadeDuration = (effect switch { 4959 => 33, 5251 or 6200 => 41, 6036 => 91, _ => 0 }) / 60.0;
        if (_sceneFadeDuration <= 0) return false;
        _scriptPause = _sceneFadeDuration;
        PrepareSceneCover(2);
        UpdateSceneTransition();
        return true;
    }
}
