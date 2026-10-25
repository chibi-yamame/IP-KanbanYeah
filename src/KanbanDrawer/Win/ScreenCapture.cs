using System;
using System.Runtime.InteropServices;
using KanbanDrawer.Core;

namespace KanbanDrawer.Win
{
    /// <summary>Copies screen pixels (physical coordinates, all monitors) with GDI BitBlt. The cursor is not included.</summary>
    internal static class ScreenCapture
    {
        public static Native.RECT VirtualScreen()
        {
            int x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            return new Native.RECT
            {
                Left = x, Top = y,
                Right = x + Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
                Bottom = y + Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN),
            };
        }

        public static Raster Capture(int x, int y, int w, int h)
        {
            var bmi = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr mem = Native.CreateCompatibleDC(screen);
            IntPtr dib = Native.CreateDIBSection(screen, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            try
            {
                if (dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
                IntPtr old = Native.SelectObject(mem, dib);
                Native.BitBlt(mem, 0, 0, w, h, screen, x, y, Native.SRCCOPY | Native.CAPTUREBLT);
                Native.SelectObject(mem, old);
                var px = new int[w * h];
                Marshal.Copy(bits, px, 0, px.Length);
                for (int i = 0; i < px.Length; i++) px[i] |= unchecked((int)0xFF000000);
                return new Raster(w, h, px);
            }
            finally
            {
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>The whole virtual screen and its top-left (raster (0,0) = screen (left, top)).</summary>
        public static Raster CaptureAll(out int left, out int top)
        {
            var r = VirtualScreen();
            left = r.Left; top = r.Top;
            return Capture(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
    }
}
