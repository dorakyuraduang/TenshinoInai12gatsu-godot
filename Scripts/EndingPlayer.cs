using System;
using Godot;

namespace Tenshi;

public partial class EndingPlayer : Control
{
    public event Action? Finished;
    public Texture2D[] Credits { get; set; } = [];
    public Texture2D? Strip { get; set; }
    public MessageView Intro { get; private set; } = null!;
    public bool CanSkip { get; set; }
    public double Elapsed { get; private set; }
    private string[] _introLines = [];
    private int _introStage;
    private static readonly int[] Groups = [2,4,6,9,11,13,19,21,24,29,31,33,39,45,50,54,56,59,68,79,81,83];
    private static readonly int[] Counts = [2,2,3,2,2,6,2,3,5,2,2,6,6,5,4,2,3,9,11,2,2,2];

    public override void _Ready()
    {
        Size = new Vector2(800, 600);
        Intro = new MessageView { Position = new Vector2(74, 20) };
        Intro.ConfigureLayout(true);
        AddChild(Intro);
        Visible = false;
        GuiInput += input =>
        {
            if (CanSkip && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Finish();
        };
    }
    public void Play(string[] lines, bool canSkip)
    {
        _introLines = lines;
        _introStage = 0;
        Elapsed = 0;
        CanSkip = canSkip;
        Intro.SetMessage(lines.Length > 0 ? lines[0] : "");
        Intro.Visible = true;
        Visible = true;
        QueueRedraw();
    }
    public void Finish() { Visible = false; Finished?.Invoke(); }
    public override void _Process(double delta)
    {
        if (!Visible) return;
        Elapsed += delta;
        int stage = Math.Min(2, (int)(Elapsed / 3));
        if (stage > _introStage && stage < _introLines.Length)
        { Intro.AppendMessage(_introLines[stage]); _introStage = stage; }
        Intro.Tick(delta, 5, false);
        Intro.Visible = Elapsed < 13.28;
        Intro.Modulate = new Color(1, 1, 1, (float)Math.Clamp((13.28 - Elapsed) / 4.28, 0, 1));
        QueueRedraw();
        if (Elapsed >= 273.5) Finish();
    }
    public override void _Draw()
    {
        if (!Visible) return;
        double t = Elapsed;
        DrawRect(new Rect2(0, 0, 800, 600), Colors.Black);
        if (t < 9 || Credits.Length < 85 || Strip == null) return;
        if (t < 14.78)
        {
            DrawRect(new Rect2(0, 0, 800, 600), new Color(1, 1, 1, (float)Math.Clamp((t - 9) / 4.28, 0, 1)));
            return;
        }
        if (t < 22.03)
        {
            DrawTexture(Credits[1], new Vector2(0, 288));
            DrawRect(new Rect2(0, 0, 800, 600), new Color(1, 1, 1, (float)Math.Clamp(1 - (t - 14.78) / 4.25, 0, 1)));
            return;
        }
        float scroll = (float)Math.Max(-5340, -(t - 22.03) * 20);
        float ornament = (float)(-(t - 22.03) * 180 % 50);
        for (int i = 0; i < 3; i++)
        {
            DrawTexture(Credits[0], new Vector2(20, ornament + i * 250));
            DrawTexture(Credits[0], new Vector2(380, ornament + i * 250));
        }
        DrawTexture(Strip, new Vector2(60, scroll));
        double remaining = t - 25.646;
        for (int group = 0; group < Groups.Length && remaining > 0; group++)
        {
            double duration = 2.126 + 3.5 + (Counts[group] - 2) * 1.1 + 2.066 + 1;
            if (remaining > duration) { remaining -= duration; continue; }
            float headerAlpha = remaining < 2.126 ? 0 : (float)Math.Clamp((remaining - 2.126) * 60 / 128, 0, 1);
            float namesAlpha = (float)Math.Clamp((remaining - 2.126 - (group == 17 ? 0 : 55.0 / 60)) * 60 / 128, 0, 1);
            float fade = (float)Math.Clamp(duration - remaining, 0, 1);
            int y = (600 - (24 + 24 * Counts[group])) / 2;
            for (int row = 0; row < Counts[group]; row++)
            {
                float alpha = (row == 0 ? headerAlpha : namesAlpha) * fade;
                DrawTexture(Credits[Groups[group] + row], new Vector2(420, y), new Color(1, 1, 1, alpha));
                y += 24;
                if (group == 17 && row is 1 or 4) y += 24;
            }
            break;
        }
        if (t < 24.146)
        {
            float alpha = (float)Math.Clamp(1 - (t - 22.03) / 2.116, 0, 1);
            DrawTexture(Credits[1], new Vector2(0, 288), new Color(1, 1, 1, alpha));
        }
        if (t > 258.75)
            DrawRect(new Rect2(0, 0, 800, 600), new Color(0, 0, 0, (float)Math.Clamp((t - 258.75) / 4.25, 0, 1)));
    }
}
