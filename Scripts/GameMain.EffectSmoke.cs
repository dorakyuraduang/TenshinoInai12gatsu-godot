using System;
using System.IO;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private async void SmokeEffects()
    {
        try
        {
            _menu.Visible = _game.Visible = _splash.Visible = _nameScreen.Visible = false;
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Effects test requires GPU");
            _preferences.Effects = true;
            var viewport = new SubViewport { Size = new Vector2I(800, 600), TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            viewport.AddChild(new ColorRect { Size = new Vector2(800, 600), Color = Colors.Blue });
            _sceneCover.Reparent(viewport);
            using Image source = Image.CreateEmpty(800, 600, false, Image.Format.Rgba8);
            source.Fill(Colors.Red);
            foreach (int effect in new[] { 2, 3, 12, 30, 31, 32, 33, 34, 35, 41, 50 })
            {
                _sceneSnapshot = ImageTexture.CreateFromImage(source);
                if (!BeginSceneTransition(effect)) throw new InvalidDataException($"Effect {effect} was skipped");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image first = viewport.GetTexture().GetImage();
                float firstRed = MeanRed(first);
                _scriptPause = _sceneFadeDuration / 2;
                UpdateSceneTransition();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image middle = viewport.GetTexture().GetImage();
                float middleRed = MeanRed(middle);
                if (firstRed < 0.5 || middleRed <= 0 || middleRed >= firstRed)
                    throw new InvalidDataException($"Effect {effect} did not transition: {firstRed}/{middleRed}");
                EndSceneTransition();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image last = viewport.GetTexture().GetImage();
                if (MeanRed(last) > 0.001f || _sceneCover.Texture != null || _sceneSnapshot != null)
                    throw new InvalidDataException($"Effect {effect} did not release its overlay");
                GD.Print($"GPU transition {effect}: source={firstRed:F3}, halfway={middleRed:F3}, completed=blue");
            }
            foreach (int effect in new[] { 4959, 5251, 6036, 6200 })
            {
                _sceneSnapshot = ImageTexture.CreateFromImage(source);
                if (!BeginScreenEffect(effect)) throw new InvalidDataException("Script motion was skipped");
                _scriptPause = _sceneFadeDuration - 8.0 / 60;
                UpdateSceneTransition();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image current = viewport.GetTexture().GetImage();
                if (MeanRed(current) <= 0) throw new InvalidDataException($"Script motion {effect} rendered empty");
                EndSceneTransition();
            }
            _sceneTone.Reparent(viewport);
            foreach (int tone in new[] { 42, 43 })
            {
                SetSceneTone(tone);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image toned = viewport.GetTexture().GetImage();
                float expected = tone == 42 ? 32f / 255 : 223f / 255;
                if (Math.Abs(MeanRed(toned) - expected) > 0.005)
                    throw new InvalidDataException($"Native green-channel tone {tone} differs from the kernel");
            }
            SetSceneTone(0);
            _sceneTone.Reparent(_game);
            var logoBlend = (ShaderMaterial)_logoLayers[0].Material.Duplicate();
            logoBlend.SetShaderParameter("channel_alpha", new Vector3(0.5f, 0.25f, 0.75f));
            using var colorTexture = ImageTexture.CreateFromImage(source);
            var coloredLayer = new TextureRect { Texture = colorTexture, Size = new Vector2(800, 600), Material = logoBlend };
            viewport.AddChild(coloredLayer);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (Image blended = viewport.GetTexture().GetImage())
            {
                Color center = blended.GetPixel(400, 300);
                if (Math.Abs(center.R - 0.5) > 0.005 || center.G > 0.005 || Math.Abs(center.B - 0.25) > 0.005)
                    throw new InvalidDataException("Logo RGB-channel interpolation differs from native draw mode 6");
            }
            if (OriginalEffectMath.Sin(64) != 256 || OriginalEffectMath.Sin(192) != -256 ||
                OriginalEffectMath.Sample(4959, 4).Shift.X != 51 ||
                OriginalEffectMath.Sample(6200, 40).Shift != Vector2.Zero)
                throw new InvalidDataException("Native motion table or completion is incorrect");
            _sceneCover.Reparent(_game);
            viewport.QueueFree();
            GD.Print("GPU effects passed: original four mask resources, 11 transitions, four frame-driven motions, two native color kernels and Logo channel blending; only synthetic colors rendered, no images exported");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static float MeanRed(Image image)
    {
        image.Convert(Image.Format.Rgba8);
        byte[] bytes = image.GetData();
        long red = 0;
        for (int i = 0; i < bytes.Length; i += 4) red += bytes[i];
        return red / (255f * bytes.Length / 4);
    }
}
