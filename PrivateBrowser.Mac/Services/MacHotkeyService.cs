using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace PrivateBrowser.Mac.Services
{
    public sealed class MacHotkeyService : IDisposable
    {
        public event Action<HotkeyAction>? HotkeyPressed;

        private readonly List<IntPtr> _hotkeys = new();
        private EventHandlerProc? _handler;
        private IntPtr _handlerRef;

        public enum HotkeyAction
        {
            CopyLatest,
            CopyAll,
            CopyLatestAndSend,
            PasteAndSend,
            ToggleWindow,
            Screenshot
        }

        public void Register()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            _handler = HandleEvent;
            IntPtr target = IntPtr.Zero;
            InstallEventHandler(
                GetApplicationEventTarget(),
                _handler,
                1,
                new[] { new EventTypeSpec { eventClass = 0x686B6579, eventKind = 5 } },
                IntPtr.Zero,
                out _handlerRef
            );

            Register(HotkeyAction.CopyLatest, 8);          // C
            Register(HotkeyAction.CopyAll, 0);             // A
            Register(HotkeyAction.CopyLatestAndSend, 1);   // S
            Register(HotkeyAction.PasteAndSend, 9);        // V
            Register(HotkeyAction.ToggleWindow, 12);       // Q
            Register(HotkeyAction.Screenshot, 7);          // X
        }

        public void Dispose()
        {
            foreach (IntPtr id in _hotkeys)
            {
                UnregisterEventHotKey(id);
            }

            _hotkeys.Clear();

            if (_handlerRef != IntPtr.Zero)
            {
                RemoveEventHandler(_handlerRef);
                _handlerRef = IntPtr.Zero;
            }
        }

        private void Register(
            HotkeyAction action,
            uint keyCode
        )
        {
            // cmdKey (256) + shiftKey (512)
            int status = RegisterEventHotKey(
                keyCode,
                256 | 512,
                new EventHotKeyID { signature = 0x50425257, id = (uint)action + 1 },
                GetApplicationEventTarget(),
                0,
                out IntPtr hotkeyRef
            );

            if (status == 0 && hotkeyRef != IntPtr.Zero)
            {
                _hotkeys.Add(hotkeyRef);
            }
        }

        private int HandleEvent(
            IntPtr nextHandler,
            IntPtr theEvent,
            IntPtr userData
        )
        {
            EventHotKeyID hotkeyId = new();
            GetEventParameter(
                theEvent,
                0x686B6964,
                0x686B6964,
                IntPtr.Zero,
                (uint)Marshal.SizeOf<EventHotKeyID>(),
                out _,
                ref hotkeyId
            );

            if (hotkeyId.id >= 1 && hotkeyId.id <= 6)
            {
                HotkeyAction action =
                    (HotkeyAction)(hotkeyId.id - 1);

                Dispatcher.UIThread.Post(
                    () => HotkeyPressed?.Invoke(action)
                );
            }

            return 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EventTypeSpec
        {
            public uint eventClass;
            public uint eventKind;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EventHotKeyID
        {
            public uint signature;
            public uint id;
        }

        private delegate int EventHandlerProc(
            IntPtr nextHandler,
            IntPtr theEvent,
            IntPtr userData
        );

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern IntPtr GetApplicationEventTarget();

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int InstallEventHandler(
            IntPtr target,
            EventHandlerProc handler,
            int numTypes,
            EventTypeSpec[] list,
            IntPtr userData,
            out IntPtr handlerRef
        );

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int RemoveEventHandler(
            IntPtr handlerRef
        );

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int RegisterEventHotKey(
            uint keyCode,
            uint modifiers,
            EventHotKeyID hotkeyId,
            IntPtr target,
            uint options,
            out IntPtr hotkeyRef
        );

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int UnregisterEventHotKey(
            IntPtr hotkeyRef
        );

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int GetEventParameter(
            IntPtr theEvent,
            uint paramName,
            uint paramType,
            IntPtr actualType,
            uint bufferSize,
            out uint actualSize,
            ref EventHotKeyID data
        );
    }
}
