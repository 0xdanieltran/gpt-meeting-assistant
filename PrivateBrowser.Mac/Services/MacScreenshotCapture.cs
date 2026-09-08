using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PrivateBrowser.Mac.Services
{
    public static class MacScreenshotCapture
    {
        public static string? CaptureMainDisplayPng()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return null;
            }

            string folder = Path.Combine(
                Path.GetTempPath(),
                "PrivateBrowser"
            );

            Directory.CreateDirectory(folder);

            string path = Path.Combine(
                folder,
                $"screenshot-{Guid.NewGuid():N}.png"
            );

            if (TryCaptureWithCoreGraphics(path) &&
                IsUsablePng(path))
            {
                return path;
            }

            if (TryCaptureWithScreencapture(path) &&
                IsUsablePng(path))
            {
                return path;
            }

            return null;
        }

        public static void CopyPngToPasteboard(
            string pngPath
        )
        {
            if (!OperatingSystem.IsMacOS() ||
                string.IsNullOrWhiteSpace(pngPath) ||
                !File.Exists(pngPath))
            {
                return;
            }

            IntPtr pasteboardClass = objc_getClass("NSPasteboard");
            IntPtr pasteboard = objc_msgSend(
                pasteboardClass,
                sel_registerName("generalPasteboard")
            );

            if (pasteboard == IntPtr.Zero)
            {
                return;
            }

            objc_msgSend(
                pasteboard,
                sel_registerName("clearContents")
            );

            IntPtr nsPath = NsString(pngPath);
            IntPtr image = objc_msgSend(
                objc_getClass("NSImage"),
                sel_registerName("alloc")
            );
            image = objc_msgSend_IntPtr(
                image,
                sel_registerName("initWithContentsOfFile:"),
                nsPath
            );

            if (image != IntPtr.Zero)
            {
                IntPtr items = objc_msgSend_IntPtr(
                    objc_getClass("NSArray"),
                    sel_registerName("arrayWithObject:"),
                    image
                );

                objc_msgSend_IntPtr(
                    pasteboard,
                    sel_registerName("writeObjects:"),
                    items
                );

                objc_msgSend(
                    image,
                    sel_registerName("release")
                );
            }

            IntPtr data = objc_msgSend_IntPtr(
                objc_getClass("NSData"),
                sel_registerName("dataWithContentsOfFile:"),
                nsPath
            );

            if (data == IntPtr.Zero)
            {
                return;
            }

            IntPtr type = NsString("public.png");
            IntPtr types = objc_msgSend_IntPtr(
                objc_getClass("NSArray"),
                sel_registerName("arrayWithObject:"),
                type
            );

            objc_msgSend_IntPtr_IntPtr(
                pasteboard,
                sel_registerName("addTypes:owner:"),
                types,
                IntPtr.Zero
            );
            objc_msgSend_IntPtr_IntPtr(
                pasteboard,
                sel_registerName("setData:forType:"),
                data,
                type
            );
        }

        public static void SendCommandV()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            int pid = Environment.ProcessId;
            IntPtr commandDown = CGEventCreateKeyboardEvent(
                IntPtr.Zero,
                kVK_Command,
                true
            );
            IntPtr vDown = CGEventCreateKeyboardEvent(
                IntPtr.Zero,
                kVK_ANSI_V,
                true
            );
            IntPtr vUp = CGEventCreateKeyboardEvent(
                IntPtr.Zero,
                kVK_ANSI_V,
                false
            );
            IntPtr commandUp = CGEventCreateKeyboardEvent(
                IntPtr.Zero,
                kVK_Command,
                false
            );

            try
            {
                CGEventSetFlags(commandDown, kCGEventFlagMaskCommand);
                CGEventSetFlags(vDown, kCGEventFlagMaskCommand);
                CGEventSetFlags(vUp, kCGEventFlagMaskCommand);

                CGEventPostToPid(pid, commandDown);
                CGEventPostToPid(pid, vDown);
                CGEventPostToPid(pid, vUp);
                CGEventPostToPid(pid, commandUp);
            }
            finally
            {
                Release(commandDown);
                Release(vDown);
                Release(vUp);
                Release(commandUp);
            }
        }

        private static bool TryCaptureWithCoreGraphics(
            string path
        )
        {
            IntPtr image = IntPtr.Zero;
            IntPtr url = IntPtr.Zero;
            IntPtr type = IntPtr.Zero;
            IntPtr destination = IntPtr.Zero;

            try
            {
                image = CGDisplayCreateImage(CGMainDisplayID());
                if (image == IntPtr.Zero)
                {
                    return false;
                }

                byte[] pathBytes = Encoding.UTF8.GetBytes(path);
                url = CFURLCreateFromFileSystemRepresentation(
                    IntPtr.Zero,
                    pathBytes,
                    pathBytes.Length,
                    false
                );

                type = CFStringCreateWithCString(
                    IntPtr.Zero,
                    "public.png",
                    kCFStringEncodingUTF8
                );

                if (url == IntPtr.Zero || type == IntPtr.Zero)
                {
                    return false;
                }

                destination = CGImageDestinationCreateWithURL(
                    url,
                    type,
                    1,
                    IntPtr.Zero
                );

                if (destination == IntPtr.Zero)
                {
                    return false;
                }

                CGImageDestinationAddImage(
                    destination,
                    image,
                    IntPtr.Zero
                );

                return CGImageDestinationFinalize(destination);
            }
            finally
            {
                Release(destination);
                Release(type);
                Release(url);
                Release(image);
            }
        }

        private static bool TryCaptureWithScreencapture(
            string path
        )
        {
            string tool = File.Exists("/usr/sbin/screencapture")
                ? "/usr/sbin/screencapture"
                : "/usr/bin/screencapture";

            if (!File.Exists(tool))
            {
                return false;
            }

            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = tool,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    }
                };

                process.StartInfo.ArgumentList.Add("-x");
                process.StartInfo.ArgumentList.Add("-D");
                process.StartInfo.ArgumentList.Add(CGMainDisplayID().ToString());
                process.StartInfo.ArgumentList.Add(path);
                process.Start();

                if (!process.WaitForExit(8000))
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                    }

                    return false;
                }

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"screencapture failed: {ex}"
                );
                return false;
            }
        }

        private static bool IsUsablePng(
            string path
        )
        {
            try
            {
                return File.Exists(path) &&
                    new FileInfo(path).Length > 32;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr NsString(
            string value
        )
        {
            return objc_msgSend_string(
                objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"),
                value
            );
        }

        private static void Release(
            IntPtr cf
        )
        {
            if (cf != IntPtr.Zero)
            {
                CFRelease(cf);
            }
        }

        private const uint kCFStringEncodingUTF8 = 0x08000100;
        private const ushort kVK_ANSI_V = 0x09;
        private const ushort kVK_Command = 0x37;
        private const ulong kCGEventFlagMaskCommand = 0x100000;

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern uint CGMainDisplayID();

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGDisplayCreateImage(
            uint display
        );

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventCreateKeyboardEvent(
            IntPtr source,
            ushort virtualKey,
            [MarshalAs(UnmanagedType.I1)] bool keyDown
        );

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventSetFlags(
            IntPtr cgEvent,
            ulong flags
        );

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventPostToPid(
            int pid,
            IntPtr cgEvent
        );

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(
            IntPtr cf
        );

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern IntPtr CFStringCreateWithCString(
            IntPtr allocator,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string str,
            uint encoding
        );

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern IntPtr CFURLCreateFromFileSystemRepresentation(
            IntPtr allocator,
            byte[] buffer,
            nint bufLen,
            [MarshalAs(UnmanagedType.I1)] bool isDirectory
        );

        [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
        private static extern IntPtr CGImageDestinationCreateWithURL(
            IntPtr url,
            IntPtr type,
            nint count,
            IntPtr options
        );

        [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
        private static extern void CGImageDestinationAddImage(
            IntPtr dest,
            IntPtr image,
            IntPtr properties
        );

        [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGImageDestinationFinalize(
            IntPtr dest
        );

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr objc_getClass(
            string name
        );

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr sel_registerName(
            string name
        );

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(
            IntPtr receiver,
            IntPtr selector
        );

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(
            IntPtr receiver,
            IntPtr selector,
            IntPtr arg
        );

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr(
            IntPtr receiver,
            IntPtr selector,
            IntPtr arg1,
            IntPtr arg2
        );

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_string(
            IntPtr receiver,
            IntPtr selector,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8
        );
    }
}
