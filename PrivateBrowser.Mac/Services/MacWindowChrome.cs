using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace PrivateBrowser.Mac.Services
{
    public static class MacWindowChrome
    {
        public static void ApplyShareExclusion(
            Window window
        )
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            try
            {
                IntPtr nsWindow =
                    window.TryGetPlatformHandle()?.Handle ??
                    IntPtr.Zero;

                if (nsWindow == IntPtr.Zero)
                {
                    return;
                }

                // NSWindow.sharingType = NSWindowSharingNone (0)
                IntPtr selector = sel_registerName("setSharingType:");
                objc_msgSend_uint(nsWindow, selector, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Share exclusion failed: {ex}"
                );
            }
        }

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr sel_registerName(
            string name
        );

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_uint(
            IntPtr receiver,
            IntPtr selector,
            uint value
        );
    }
}
