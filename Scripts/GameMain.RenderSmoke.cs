using System;
using System.IO;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private async void SmokeRender()
    {
        try
        {
            _menu.Visible = _game.Visible = _splash.Visible = _nameScreen.Visible = false;
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Render smoke requires a real renderer");
            var viewport = new SubViewport { Size = new Vector2I(800, 600), TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var text = new MessageView { Position = new Vector2(154, 504) };
            viewport.AddChild(text);
            text.Text = new string('文', 57);
            text.Tick(1.0 / 60, 5, false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image partial = viewport.GetTexture().GetImage();
            int partialPixels = CountPixels(partial, new Rect2I(154, 504, 494, 90));
            text.RevealAll();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image complete = viewport.GetTexture().GetImage();
            int fullPixels = CountPixels(complete, new Rect2I(154, 504, 494, 90));
            if (partialPixels < 1 || fullPixels <= partialPixels * 20)
                throw new InvalidDataException($"Actual gradual/full rendering did not differ: {partialPixels}/{fullPixels}");
            text.Text = "甲<$p36乙A>丙";
            text.RevealAll();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image styled = viewport.GetTexture().GetImage();
            int styledPixels = CountPixels(styled, new Rect2I(154, 504, 494, 90));
            if (styledPixels < 100 || text.Pages[0][1].FontSize != 36)
                throw new InvalidDataException("Styled native glyphs did not render inside the dialogue surface");
            text.ConfigureLayout(true);
            text.Position = new Vector2(74, 20);
            text.Text = new string('文', 288);
            text.RevealAll();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image fullPage = viewport.GetTexture().GetImage();
            int pagePixels = CountPixels(fullPage, new Rect2I(74, 20, 654, 520));
            if (pagePixels < fullPixels * 3) throw new InvalidDataException("Full-page rasterization did not fill the expected rows");
            viewport.QueueFree();
            GD.Print($"GPU text check passed: gradual={partialPixels}, completed={fullPixels}, styled={styledPixels}, full-page={pagePixels}; no pixels outside original text surfaces; no images exported");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static int CountPixels(Image image, Rect2I bounds)
    {
        image.Convert(Image.Format.Rgba8);
        byte[] bytes = image.GetData();
        int count = 0, width = image.GetWidth();
        for (int pixel = 0; pixel < bytes.Length / 4; pixel++)
        {
            if (bytes[pixel * 4 + 3] == 0) continue;
            if (!bounds.HasPoint(new Vector2I(pixel % width, pixel / width)))
                throw new InvalidDataException("Rendered glyph escaped the clipping rectangle");
            count++;
        }
        return count;
    }
}
