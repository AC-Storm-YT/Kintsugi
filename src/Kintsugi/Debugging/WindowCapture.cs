#if DEBUG
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Kintsugi.Debugging;

/// <summary>
/// Development aid: captures a window exactly as Windows draws it (title bar and frame included)
/// for README screenshots. Not compiled into Release builds.
/// </summary>
internal static partial class WindowCapture
{
    private const uint PrintWindowRenderFullContent = 0x2;
    private const int DwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

    public static void SavePng(Window window, string path)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out Rect outer);
        int width = outer.Right - outer.Left;
        int height = outer.Bottom - outer.Top;

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = CreateCompatibleBitmap(screen, width, height);
        IntPtr previous = SelectObject(memory, bitmap);
        try
        {
            PrintWindow(hwnd, memory, PrintWindowRenderFullContent);
            SelectObject(memory, previous);

            BitmapSource full = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            // GetWindowRect includes invisible resize borders; crop to the visible frame.
            BitmapSource image = full;
            if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out Rect visible, Marshal.SizeOf<Rect>()) == 0)
            {
                image = new CroppedBitmap(full, new Int32Rect(
                    visible.Left - outer.Left, visible.Top - outer.Top,
                    visible.Right - visible.Left, visible.Bottom - visible.Top));
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using FileStream file = File.Create(path);
            encoder.Save(file);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
#endif
