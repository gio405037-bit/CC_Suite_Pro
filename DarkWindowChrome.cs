using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CCSuitePro
{
    internal static class DarkWindowChrome
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        internal static void Apply(Window window)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return;
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int enabled = 1;
                // DWM 20: modern Win10/11; 19: earlier Win10 revisions.
                if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
            }
            catch (Exception) { /* Unsupported Windows builds retain the OS title-bar theme. */ }
        }
    }
}
