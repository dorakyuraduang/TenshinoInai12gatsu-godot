using System;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private void SetWeather(int flags)
    {
        if (flags == 0) { _weather.Stop(); return; }
        if (_weather.Textures.Length == 0)
        {
            byte[] data = _uiArchive!.Read("snow.px").Data;
            var frames = UiPxDecoder.DecodeFrames(data, Enumerable.Range(0, UiPxDecoder.FrameCount(data)).ToArray());
            _weather.Origins = frames.Select(f => (Vector2)f.Origin).ToArray();
            _weather.Textures = frames.Select(f => { using (f.Image) return (Texture2D)ImageTexture.CreateFromImage(f.Image); }).ToArray();
        }
        _weather.Start(flags);
        ApplyWeatherPreferences();
    }

    private void ApplyWeatherPreferences() => _weather.Visible = _weather.Flags != 0 && _preferences.Effects && _preferences.Particles;
}

// #AppEffect:1444/1674/2243/2638/3150/3431/4115. All sprites are snow.px frames.
// Native particle objects integrate 12-bit fixed-point velocities every tick.
public partial class OriginalWeather : Control
{
    private sealed class Particle
    {
        internal float X, Y, Vx, Vy, Ax, Z, DriftX, DriftY;
        internal int Delay, Depth, Frame;
        internal float Alpha = 1;
        internal bool Started;
    }
    public Texture2D[] Textures { get; set; } = [];
    public Vector2[] Origins { get; set; } = [];
    public int Flags { get; private set; }
    public int ActiveParticles => _particles.Count(p => p.Started && p.Delay == 0);
    private readonly Particle[] _particles = Enumerable.Range(0, 120).Select(_ => new Particle()).ToArray();
    private readonly Random _random = new();
    private int _type, _density, _tick, _windPhase, _windX, _windY;
    private double _remainder;
    public OriginalWeather() { Size = new Vector2(800, 600); ClipContents = true; MouseFilter = MouseFilterEnum.Ignore; Visible = false; }

    public void Stop() { Flags = 0; Visible = false; }
    public void Start(int flags)
    {
        if (flags == Flags) return;
        Flags = flags;
        _type = (flags & 0xffff) >> 3;
        _density = Math.Min(120, (flags & 7) * (flags & 7) * 20);
        _tick = _windPhase = _windX = _windY = 0;
        _remainder = 0;
        for (int i = 0; i < _particles.Length; i++)
            _particles[i] = new Particle { Delay = i * (_type == 2 ? 4 : _type == 6 ? 3 : 8) };
        Visible = true;
    }

    private int Rand(int n) => n <= 0 ? 0 : _random.Next(n);
    private void Spawn(Particle p, bool first)
    {
        p.Started = true;
        p.Alpha = 1;
        p.DriftX = p.DriftY = p.Ax = 0;
        p.Vx = 0;
        if (_type is 2 or 6)
        {
            int side = Rand(4), distance = _type == 6 ? 1500 : first ? 2000 : 100;
            p.X = side < 2 ? Rand(1000) - 500 : (side == 2 ? -400 - Rand(distance) : 400 + Rand(distance));
            p.Y = side >= 2 ? Rand(800) - 400 : (side == 0 ? -300 - Rand(distance) : 300 + Rand(distance));
            p.Z = _type == 6 ? -Rand(100) : 1000 + Rand(first ? 1000 : 500);
            return;
        }
        p.X = _type == 1 ? 100 + Rand(900) : Rand(800);
        p.Y = -Rand(400);
        p.Depth = _type == 1 ? Rand(Rand(Rand(79) + 1) + 1) : Rand(Rand(79) + 1);
        if (_type == 3)
        {
            int region = Rand(7);
            p.X = region < 3 ? Rand(200) : region > 3 ? 600 + Rand(200) : 200 + Rand(400);
            p.Y = -Rand(200);
            p.Depth = Rand(79) + 1;
        }
        if (_type == 4)
        {
            p.X = (Rand(2) == 0 ? 50 : 500) + Rand(200);
            p.Y = 200 - Rand(100);
            p.Depth = Rand(80);
        }
        p.Frame = _type switch { 1 => p.Depth / 2, 3 => p.Depth * 16 / 80, 4 => p.Depth * 12 / 80, _ => p.Depth * 20 / 80 };
        p.Vy = (_type switch { 1 => 7000 + p.Depth * 300, 3 => 1500 + p.Depth * 150, _ => 5000 + p.Depth * 200 }) / 4096f;
        if (_type == 1) p.Vx = (-2500 - p.Depth * 120) / 4096f;
        if (_type == 4)
        {
            double angle = (63488 + (int)(p.X * 4096) / 304) * Math.Tau / 65536;
            p.Vy = (float)(Math.Cos(angle) * Math.Max(p.Depth, 4) / 500);
            p.Vx = (float)(Math.Sin(angle) * Math.Max(p.Depth, 4) / 500);
        }
    }

    public void Tick(double delta)
    {
        if (!Visible || Textures.Length == 0) return;
        _remainder += delta * 60;
        int steps = (int)_remainder;
        _remainder -= steps;
        for (int t = 0; t < steps; t++)
        {
            _tick++;
            if (_type is 2 or 6 && _tick % 4 == 0)
            {
                double angle = _windPhase * 10 * Math.Tau / 65536;
                int divisor = _type == 6 ? 3000 : 300;
                _windX = (int)(Math.Cos(angle) * 4096) / divisor;
                _windY = (int)(Math.Sin(angle) * 4096) / divisor;
                _windPhase += (_type == 6 ? 20 : 200) + Rand(20);
            }
            else if (_tick % 2 == 0)
            {
                _windX = OriginalEffectMath.Sin(_windPhase / (_type is 3 or 4 ? 3 : 2)) / (_type is 3 or 4 ? 30 : 23);
                _windPhase += (1 + Rand(10)) / 2;
            }
            for (int i = 0; i < _density; i++)
            {
                Particle p = _particles[i];
                if (p.Delay > 0) { p.Delay--; continue; }
                if (!p.Started) Spawn(p, true);
                if (_type is 2 or 6)
                {
                    p.Z += _type == 6 ? 1 + Math.Max(1, (int)p.Z / 80) : -Math.Max(1, (int)p.Z / 65);
                    if ((_type == 2 && p.Z <= 0) || (_type == 6 && p.Z >= 1000)) Spawn(p, false);
                    p.DriftX += _windX; p.DriftY += _windY;
                    p.X += (int)p.DriftX / 1200; p.Y += (int)p.DriftY / 1200;
                    p.Frame = p.Z > 1000 || p.Z < 0 ? -1 : Math.Clamp((int)p.Z * 40 / 1000, 0, 39);
                    p.Alpha = _type == 6 ? Math.Clamp((1000 - p.Z) / 500, 0, 1) : Math.Clamp(p.Z / 400, 0, 1);
                }
                else
                {
                    if (_tick % 2 == 0)
                    {
                        p.Ax = (_windX * (1 + p.Depth / 8) / 2) / 4096f;
                        if (p.Y > (_type == 4 ? 500 : 200)) p.Alpha = Math.Max(1f / 128, p.Alpha - 3f / 128);
                    }
                    p.Vx += p.Ax;
                    p.X += p.Vx; p.Y += p.Vy;
                    if (p.Y > 600) Spawn(p, false);
                }
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Visible) return;
        for (int i = 0; i < _density; i++)
        {
            Particle p = _particles[i];
            if (!p.Started || p.Delay > 0 || p.Frame < 0 || p.Frame >= Textures.Length) continue;
            Vector2 position = new(p.X, p.Y);
            if (_type is 2 or 6)
            {
                float factor = (_type == 6 ? (180000 + p.Z * 1800) : (280000 + p.Z * 1000)) / (300 * 4096);
                position = new Vector2(400, 300) + position * factor;
            }
            DrawTexture(Textures[p.Frame], position + Origins[p.Frame], new Color(1, 1, 1, p.Alpha));
        }
    }
}
