using System;
using Godot;

namespace Tenshi;

internal static class OriginalEffectMath
{
    // IDA 100174F0 / 10017500 index this table; retain the native rounding.
    private static readonly short[] HalfSine = [
        0,6,12,18,25,31,37,44,50,56,62,68,74,80,86,92,98,104,110,115,121,127,132,137,143,148,153,158,
        163,168,173,177,182,186,190,195,199,203,206,210,214,217,220,224,227,230,232,235,237,240,242,244,
        246,248,249,251,252,253,254,255,255,256,256,256,256,256,256,256,255,254,253,252,251,250,248,247,
        245,243,241,239,236,234,231,228,225,222,219,215,212,208,205,201,197,193,188,184,180,175,170,166,
        161,156,151,145,140,135,129,124,118,113,107,101,95,89,83,77,71,65,59,53,47,40,34,28,22,15,9,3];
    internal static int Sin(int angle) { int i = angle & 255; return i < 128 ? HalfSine[i] : -HalfSine[255 - i]; }
    internal readonly record struct SampleValue(Vector2 Shift, float Zoom = 1, float Flash = 0);

    internal static SampleValue Sample(int effect, int frame)
    {
        if (effect == 4959) return new SampleValue(new Vector2(frame < 32 ? Sin(frame * 16) / 5 : 0, 0));
        if (effect == 6200)
        {
            int denominator = (400 + frame * (frame - 1)) / 100;
            return new SampleValue(new Vector2(0, frame < 40 ? Sin(frame * 20) / denominator : 0));
        }
        if (effect == 5251 && frame < 40)
        {
            int denominator = (2 + 10 * frame + 2 * frame * (frame - 1)) / 100;
            // Native integer division by zero saturates; an off-screen source is black.
            int numerator = Sin(frame * 14 + 64);
            int x = denominator == 0 ? Math.Sign(numerator) * 32767 : numerator / denominator;
            return new SampleValue(new Vector2(x, 0), Flash: Math.Max(0, 128 - 6 * frame) / 128f);
        }
        if (effect == 6036)
            return new SampleValue(Vector2.Zero, (frame % 5) switch { 1 => 4096f / 4032, 2 or 3 => 4096f / 3968, _ => 1 });
        return new SampleValue(Vector2.Zero);
    }
}
