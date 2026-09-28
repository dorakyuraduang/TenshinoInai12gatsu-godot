using System;
using System.Runtime.InteropServices;

namespace Tenshi;

// Like the original engine's font selector, this opens the Windows font dialog.
internal static class NativeFontPicker
{
    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ChooseFont
    {
        public uint Size;
        public IntPtr Owner, DC, LogFont;
        public int PointSize;
        public uint Flags, Color;
        public IntPtr Custom, Hook, Template, Instance, Style;
        public ushort FontType;
        public int MinSize, MaxSize;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseFontW(ref ChooseFont data);
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();

    public static string? Pick(string current)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var font = new LogFont { Height = -24, CharSet = 134, FaceName = current };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<LogFont>());
        try
        {
            Marshal.StructureToPtr(font, memory, false);
            var dialog = new ChooseFont { Size = (uint)Marshal.SizeOf<ChooseFont>(), Owner = GetActiveWindow(),
                LogFont = memory, Flags = 0x00000001 | 0x00000040 | 0x00010000 | 0x00100000 | 0x00200000 };
            return ChooseFontW(ref dialog) ? Marshal.PtrToStructure<LogFont>(memory).FaceName : null;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
