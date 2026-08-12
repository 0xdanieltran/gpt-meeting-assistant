using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Text.Json;

using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

// Explicit WPF aliases prevent ambiguity after <UseWindowsForms>true</UseWindowsForms>.
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfClipboard = System.Windows.Clipboard;

namespace PrivateBrowser
{
    public partial class MainWindow : Window
    {
        // =========================================================
        // WINDOW HANDLE
        // =========================================================

        private IntPtr _windowHandle = IntPtr.Zero;

        private readonly Dictionary<string, WpfBrush>
            _speakerBrushes =
                new Dictionary<string, WpfBrush>(
                    StringComparer.OrdinalIgnoreCase
                );

        private Forms.NotifyIcon? _trayIcon;

        private bool _isExiting = false;

        // =========================================================
        // HOTKEY SHOW/HIDE STATE
        // =========================================================

        private bool _browserHiddenByHotkey = false;

        private double _savedLeft;
        private double _savedTop;

        private bool _savedPositionValid = false;


        private readonly WpfBrush[] _speakerPalette =
        {
            new WpfSolidColorBrush(
                WpfColor.FromRgb(20, 20, 20)
            ),

            new WpfSolidColorBrush(
                WpfColor.FromRgb(45, 45, 45)
            ),

            new WpfSolidColorBrush(
                WpfColor.FromRgb(70, 70, 70)
            ),

            new WpfSolidColorBrush(
                WpfColor.FromRgb(92, 92, 92)
            ),

            new WpfSolidColorBrush(
                WpfColor.FromRgb(110, 110, 110)
            ),

            new WpfSolidColorBrush(
                WpfColor.FromRgb(55, 55, 55)
            )
        };

        private WpfBrush GetSpeakerBrush(
            string? speaker
        )
        {
            string key =
                string.IsNullOrWhiteSpace(
                    speaker
                )
                    ? "Unknown speaker"
                    : speaker.Trim();


            if (
                _speakerBrushes.TryGetValue(
                    key,
                    out WpfBrush? existing
                )
            )
            {
                return existing;
            }


            int index =
                _speakerBrushes.Count %
                _speakerPalette.Length;


            WpfBrush brush =
                _speakerPalette[index];


            _speakerBrushes[key] =
                brush;


            return brush;
        }

        private void RenderTranscript(
            IReadOnlyList<CaptionItem> history
        )
        {
            TranscriptDocument.Blocks.Clear();


            if (
                history == null ||
                history.Count == 0
            )
            {
                Paragraph empty =
                    new Paragraph(
                        new Run(
                            "Waiting for meeting captions..."
                        )
                    );


                empty.Foreground =
                    new WpfSolidColorBrush(
                        WpfColor.FromRgb(
                            120,
                            120,
                            120
                        )
                    );

                empty.Margin =
                    new Thickness(
                        0
                    );


                TranscriptDocument.Blocks.Add(
                    empty
                );

                return;
            }


            foreach (
                CaptionItem item
                in history
            )
            {
                string speaker =
                    string.IsNullOrWhiteSpace(
                        item.Speaker
                    )
                        ? "Unknown speaker"
                        : item.Speaker;


                // =====================================================
                // ONE CAPTION = ONE PARAGRAPH
                // =====================================================

                Paragraph paragraph =
                    new Paragraph();


                paragraph.Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        14
                    );


                paragraph.LineHeight =
                    21;


                // =====================================================
                // SPEAKER
                // =====================================================

                Run speakerRun =
                    new Run(
                        speaker
                    );


                speakerRun.FontWeight =
                    FontWeights.Bold;

                speakerRun.FontSize =
                    13;

                speakerRun.Foreground =
                    GetSpeakerBrush(
                        speaker
                    );


                paragraph.Inlines.Add(
                    speakerRun
                );


                // =====================================================
                // NEW LINE
                // =====================================================

                paragraph.Inlines.Add(
                    new LineBreak()
                );


                // =====================================================
                // CAPTION TEXT
                // =====================================================

                Run captionRun =
                    new Run(
                        item.Text
                    );


                captionRun.FontWeight =
                    FontWeights.Normal;

                captionRun.FontSize =
                    14;

                captionRun.Foreground =
                    new WpfSolidColorBrush(
                        WpfColor.FromRgb(
                            38,
                            38,
                            38
                        )
                    );


                paragraph.Inlines.Add(
                    captionRun
                );


                TranscriptDocument.Blocks.Add(
                    paragraph
                );
            }


            TranscriptText.ScrollToEnd();
        }


        // =========================================================
        // CAPTION STORE + SERVER
        // =========================================================

        private readonly CaptionStore _captionStore =
            new CaptionStore();

        private CaptionServer? _captionServer;

        private GridLength _lastCaptionWidth =
            new GridLength(380);

        private bool _captionsCollapsed =
            false;

        private void CaptionStore_Changed()
        {
            Dispatcher.BeginInvoke(
                new Action(
                    UpdateCaptionPanel
                )
            );
        }

        private void UpdateCaptionPanel()
        {
            try
            {
                CaptionItem? latest =
                    _captionStore.GetLatest();


                string? meetingKey =
                    _captionStore
                        .GetCurrentMeetingKey();


                var history =
                    _captionStore
                        .GetHistory();


                // =====================================================
                // MEETING
                // =====================================================

                MeetingKeyText.Text =
                    string.IsNullOrWhiteSpace(
                        meetingKey
                    )
                        ? "No meeting"
                        : $"Meeting: {meetingKey}";


                // =====================================================
                // COUNT
                // =====================================================

                CaptionCountText.Text =
                    history.Count == 1
                        ? "1 segment"
                        : $"{history.Count} segments";


                // =====================================================
                // EMPTY
                // =====================================================

                if (
                    latest == null
                )
                {
                    LatestSpeakerText.Text =
                        "Waiting for captions...";

                    LatestCaptionText.Text =
                        "Google Meet captions will appear here.";


                    RenderTranscript(
                        history
                    );

                    return;
                }


                // =====================================================
                // LATEST SPEAKER
                // =====================================================

                LatestSpeakerText.Text =
                    string.IsNullOrWhiteSpace(
                        latest.Speaker
                    )
                        ? "Unknown speaker"
                        : latest.Speaker;


                // Match latest speaker color too.
                LatestSpeakerText.Foreground =
                    GetSpeakerBrush(
                        latest.Speaker
                    );


                // =====================================================
                // LATEST CAPTION
                // =====================================================

                LatestCaptionText.Text =
                    latest.Text;


                // =====================================================
                // FULL FORMATTED TRANSCRIPT
                // =====================================================

                RenderTranscript(
                    history
                );
            }
            catch (
                Exception ex
            )
            {
                System.Diagnostics.Debug
                    .WriteLine(
                        $"Caption panel update failed: {ex}"
                    );
            }
        }

        private void CaptionToggleButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            if (!_captionsCollapsed)
            {
                if (
                    CaptionColumn.ActualWidth > 0
                )
                {
                    _lastCaptionWidth =
                        new GridLength(
                            CaptionColumn.ActualWidth
                        );
                }


                CaptionColumn.MinWidth =
                    0;

                CaptionColumn.Width =
                    new GridLength(0);


                CaptionPanel.Visibility =
                    Visibility.Collapsed;

                CaptionSplitter.Visibility =
                    Visibility.Collapsed;


                CaptionToggleButton.Content =
                    "◀";

                CaptionToggleButton.ToolTip =
                    "Show captions";


                _captionsCollapsed =
                    true;
            }
            else
            {
                CaptionPanel.Visibility =
                    Visibility.Visible;


                CaptionColumn.MinWidth =
                    280;

                CaptionColumn.Width =
                    _lastCaptionWidth;


                CaptionSplitter.Visibility =
                    Visibility.Visible;


                CaptionToggleButton.Content =
                    "▶";

                CaptionToggleButton.ToolTip =
                    "Hide captions";


                _captionsCollapsed =
                    false;
            }
        }


        // =========================================================
        // DISPLAY AFFINITY
        // =========================================================

        private const uint WDA_NONE =
            0x00000000;

        private const uint WDA_EXCLUDEFROMCAPTURE =
            0x00000011;


        [DllImport(
            "user32.dll",
            SetLastError = true
        )]
        private static extern bool SetWindowDisplayAffinity(
            IntPtr hWnd,
            uint dwAffinity
        );


        [DllImport(
            "user32.dll",
            SetLastError = true
        )]
        private static extern bool GetWindowDisplayAffinity(
            IntPtr hWnd,
            out uint pdwAffinity
        );


        // =========================================================
        // GLOBAL HOTKEYS
        // =========================================================

        private const int HOTKEY_LATEST_CAPTION = 1001;
        private const int HOTKEY_ALL_CAPTIONS = 1002;
        private const int HOTKEY_COPY_LATEST_AND_SEND = 1003;
        private const int HOTKEY_PASTE_AND_SEND = 1004;
        private const int HOTKEY_TOGGLE_BROWSER = 1005;


        private const uint MOD_CONTROL =
            0x0002;

        private const uint MOD_SHIFT =
            0x0004;

        private const uint MOD_NOREPEAT =
            0x4000;


        private const uint VK_C = 0x43;
        private const uint VK_A = 0x41;
        private const uint VK_S = 0x53;
        private const uint VK_V = 0x56;
        private const uint VK_Q = 0x51;


        private const int WM_HOTKEY =
            0x0312;


        [DllImport(
            "user32.dll",
            SetLastError = true
        )]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk
        );


        [DllImport(
            "user32.dll",
            SetLastError = true
        )]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id
        );


        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public MainWindow()
        {
            InitializeComponent();

            InitializeTrayIcon();

            _captionStore.Changed +=
                CaptionStore_Changed;

            SourceInitialized +=
                MainWindow_SourceInitialized;

            Loaded +=
                MainWindow_Loaded;

            Closed +=
                MainWindow_Closed;

            StateChanged +=
                MainWindow_StateChanged;

        }


        // =========================================================
        // WINDOW INITIALIZATION
        // =========================================================


        private void InitializeTrayIcon()
        {
            // Dispose an existing tray icon if this method is ever called again.
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayIcon =
                new Forms.NotifyIcon();

            _trayIcon.Text =
                "ChatGPT PrivateBrowser";

            // =====================================================
            // ICON
            // =====================================================
            //
            // Use the icon embedded in the running EXE.
            // This is much more reliable than loading
            // Assets\favico.ico at runtime because published/build
            // output may not contain that Assets file.
            //
            // Since <ApplicationIcon>Assets\favico.ico</ApplicationIcon>
            // is already set in the project, the EXE contains the icon.
            // =====================================================

            try
            {
                string? exePath =
                    Environment.ProcessPath;

                if (
                    !string.IsNullOrWhiteSpace(exePath) &&
                    File.Exists(exePath)
                )
                {
                    _trayIcon.Icon =
                        Drawing.Icon.ExtractAssociatedIcon(
                            exePath
                        );
                }

                // Final fallback so NotifyIcon always has an icon.
                if (_trayIcon.Icon == null)
                {
                    _trayIcon.Icon =
                        Drawing.SystemIcons.Application;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Tray icon load failed: {ex}"
                );

                _trayIcon.Icon =
                    Drawing.SystemIcons.Application;
            }

            // =====================================================
            // TRAY MENU
            // =====================================================

            Forms.ContextMenuStrip menu =
                new Forms.ContextMenuStrip();

            Forms.ToolStripMenuItem showItem =
                new Forms.ToolStripMenuItem(
                    "Show ChatGPT PrivateBrowser"
                );

            showItem.Click +=
                (_, _) =>
                {
                    ShowPrivateBrowserFromTrayOrHotkey();
                };

            Forms.ToolStripMenuItem exitItem =
                new Forms.ToolStripMenuItem(
                    "Exit"
                );

            exitItem.Click +=
                (_, _) =>
                {
                    ExitApplication();
                };

            menu.Items.Add(
                showItem
            );

            menu.Items.Add(
                new Forms.ToolStripSeparator()
            );

            menu.Items.Add(
                exitItem
            );

            _trayIcon.ContextMenuStrip =
                menu;

            // Double-click tray icon to restore.
            _trayIcon.DoubleClick +=
                (_, _) =>
                {
                    ShowPrivateBrowserFromTrayOrHotkey();
                };

            // Also allow a normal left click to restore it.
            _trayIcon.MouseClick +=
                (_, e) =>
                {
                    if (
                        e.Button ==
                        Forms.MouseButtons.Left
                    )
                    {
                        ShowPrivateBrowserFromTrayOrHotkey();
                    }
                };

            // IMPORTANT:
            // Set Visible only after Icon and menu have been assigned.
            _trayIcon.Visible =
                true;
        }


        private void ShowPrivateBrowserFromTrayOrHotkey()
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (_browserHiddenByHotkey)
                        {
                            RestorePrivateBrowserFromHotkey();

                            return;
                        }

                        ShowPrivateBrowser();
                    }
                )
            );
        }


        private void ShowPrivateBrowser()
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (_trayIcon != null)
                        {
                            _trayIcon.Visible = true;
                        }

                        if (!IsVisible)
                        {
                            Show();
                        }

                        if (
                            WindowState ==
                            WindowState.Minimized
                        )
                        {
                            WindowState =
                                WindowState.Normal;
                        }

                        _browserHiddenByHotkey = false;

                        Activate();

                        Topmost = true;

                        Focus();
                    }
                )
            );
        }


        private void HidePrivateBrowser()
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        Hide();

                        if (_trayIcon != null)
                        {
                            _trayIcon.Visible =
                                true;
                        }
                    }
                )
            );
        }


        private void HidePrivateBrowserByHotkey()
        {
            if (_browserHiddenByHotkey)
            {
                return;
            }

            _savedLeft =
                Left;

            _savedTop =
                Top;

            _savedPositionValid =
                true;

            // Keep the same WPF window and WebView2 instance alive.
            // Move the window outside the visible desktop instead of
            // calling Hide(), which can cause a black capture surface
            // after restoring the window.
            Topmost =
                false;

            Left =
                SystemParameters.VirtualScreenLeft -
                Math.Max(
                    ActualWidth,
                    Width
                ) -
                200;

            Top =
                SystemParameters.VirtualScreenTop -
                Math.Max(
                    ActualHeight,
                    Height
                ) -
                200;

            _browserHiddenByHotkey =
                true;

            if (_trayIcon != null)
            {
                _trayIcon.Visible =
                    true;
            }
        }


        private void RestorePrivateBrowserFromHotkey()
        {
            if (!_browserHiddenByHotkey)
            {
                ShowPrivateBrowser();

                return;
            }

            if (
                WindowState ==
                WindowState.Minimized
            )
            {
                WindowState =
                    WindowState.Normal;
            }

            if (_savedPositionValid)
            {
                Left =
                    _savedLeft;

                Top =
                    _savedTop;
            }

            Topmost =
                true;

            _browserHiddenByHotkey =
                false;

            Activate();

            Focus();
        }



        private void MainWindow_StateChanged(
            object? sender,
            EventArgs e
        )
        {
            if (
                WindowState ==
                WindowState.Minimized
            )
            {
                HidePrivateBrowser();
            }
        }

        protected override void OnClosing(
            System.ComponentModel.CancelEventArgs e
        )
        {
            if (
                !_isExiting
            )
            {
                e.Cancel =
                    true;

                HidePrivateBrowser();

                return;
            }


            base.OnClosing(
                e
            );
        }

        private void ExitApplication()
        {
            _isExiting =
                true;


            if (
                _trayIcon !=
                null
            )
            {
                _trayIcon.Visible =
                    false;
            }


            Close();
        }

        private void MainWindow_SourceInitialized(
            object? sender,
            EventArgs e
        )
        {
            _windowHandle =
                new WindowInteropHelper(
                    this
                ).Handle;


            HwndSource? source =
                HwndSource.FromHwnd(
                    _windowHandle
                );

            source?.AddHook(
                WndProc
            );


            RegisterCaptionHotkeys();
        }


        // =========================================================
        // WEBVIEW2 INITIALIZATION
        // =========================================================

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e
        )
        {
            try
            {
                // -------------------------------------------------
                // START LOCAL CAPTION SERVER
                // -------------------------------------------------

                _captionServer =
                    new CaptionServer(
                        _captionStore
                    );

                await _captionServer
                    .StartAsync();


                // -------------------------------------------------
                // WEBVIEW2 PROFILE
                // -------------------------------------------------

                string profilePath =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder
                                .LocalApplicationData
                        ),
                        "PrivateBrowser",
                        "Profile"
                    );


                Directory.CreateDirectory(
                    profilePath
                );


                CoreWebView2Environment environment =
                    await CoreWebView2Environment
                        .CreateAsync(
                            null,
                            profilePath
                        );


                await Browser
                    .EnsureCoreWebView2Async(
                        environment
                    );


                // =====================================================
                // ALWAYS-ON CAPTURE EXCLUSION
                // =====================================================
                //
                // Apply once after WebView2 is fully initialized.
                // Avoid repeatedly changing display affinity when the
                // window is restored/activated because some capture paths
                // can temporarily show a black composited region.
                // =====================================================

                await Task.Delay(
                    200
                );

                bool protectedOk =
                    EnableCaptureProtection();

                System.Diagnostics.Debug.WriteLine(
                    $"Capture protection after WebView2 init: {protectedOk}"
                );


                Browser.CoreWebView2
                    .NavigationCompleted +=
                    CoreWebView2_NavigationCompleted;


                Navigate(
                    "https://chatgpt.com"
                );
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"PrivateBrowser initialization failed:\n\n{ex.Message}",
                    "PrivateBrowser",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }


        // =========================================================
        // WEB BROWSER NAVIGATION
        // =========================================================

        private void Navigate(
            string address
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    address
                )
            )
            {
                return;
            }


            address =
                address.Trim();


            if (
                !address.StartsWith(
                    "http://",
                    StringComparison.OrdinalIgnoreCase
                )
                &&
                !address.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                address =
                    "https://" +
                    address;
            }


            Browser.CoreWebView2?
                .Navigate(
                    address
                );


            AddressBar.Text =
                address;
        }


        private void CoreWebView2_NavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs e
        )
        {
            if (
                Browser.Source !=
                null
            )
            {
                AddressBar.Text =
                    Browser.Source
                        .ToString();
            }


            BackButton.IsEnabled =
                Browser.CanGoBack;


            ForwardButton.IsEnabled =
                Browser.CanGoForward;
        }


        private void GoButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            Navigate(
                AddressBar.Text
            );
        }


        private void AddressBar_KeyDown(
            object sender,
            WpfKeyEventArgs e
        )
        {
            if (
                e.Key ==
                System.Windows.Input.Key.Enter
            )
            {
                Navigate(
                    AddressBar.Text
                );
            }
        }


        private void BackButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            if (
                Browser.CanGoBack
            )
            {
                Browser.GoBack();
            }
        }


        private void ForwardButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            if (
                Browser.CanGoForward
            )
            {
                Browser.GoForward();
            }
        }


        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            Browser.Reload();
        }


        // =========================================================
        // ALWAYS-ON CAPTURE PROTECTION
        // =========================================================

        private bool EnableCaptureProtection()
        {
            if (
                _windowHandle ==
                IntPtr.Zero
            )
            {
                return false;
            }


            bool setOk =
                SetWindowDisplayAffinity(
                    _windowHandle,
                    WDA_EXCLUDEFROMCAPTURE
                );


            if (!setOk)
            {
                int setError =
                    Marshal.GetLastWin32Error();

                System.Diagnostics.Debug.WriteLine(
                    $"Capture protection failed. Win32 error: {setError}"
                );

                return false;
            }


            bool getOk =
                GetWindowDisplayAffinity(
                    _windowHandle,
                    out uint currentAffinity
                );


            bool protectedSuccessfully =
                getOk &&
                currentAffinity ==
                WDA_EXCLUDEFROMCAPTURE;


            System.Diagnostics.Debug.WriteLine(
                $"Capture protection active: {protectedSuccessfully}; " +
                $"Affinity: 0x{currentAffinity:X8}"
            );


            return protectedSuccessfully;
        }


        // =========================================================
        // GLOBAL HOTKEY REGISTRATION
        // =========================================================

        private void RegisterCaptionHotkeys()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            UnregisterHotKey(_windowHandle, HOTKEY_LATEST_CAPTION);
            UnregisterHotKey(_windowHandle, HOTKEY_ALL_CAPTIONS);
            UnregisterHotKey(_windowHandle, HOTKEY_COPY_LATEST_AND_SEND);
            UnregisterHotKey(_windowHandle, HOTKEY_PASTE_AND_SEND);
            UnregisterHotKey(_windowHandle, HOTKEY_TOGGLE_BROWSER);

            RegisterRequiredHotkey(
                HOTKEY_LATEST_CAPTION,
                VK_C,
                "Ctrl + Shift + C"
            );

            RegisterRequiredHotkey(
                HOTKEY_ALL_CAPTIONS,
                VK_A,
                "Ctrl + Shift + A"
            );

            RegisterRequiredHotkey(
                HOTKEY_COPY_LATEST_AND_SEND,
                VK_S,
                "Ctrl + Shift + S"
            );

            RegisterRequiredHotkey(
                HOTKEY_PASTE_AND_SEND,
                VK_V,
                "Ctrl + Shift + V"
            );

            RegisterRequiredHotkey(
                HOTKEY_TOGGLE_BROWSER,
                VK_Q,
                "Ctrl + Shift + Q"
            );
        }


        private void RegisterRequiredHotkey(
            int id,
            uint virtualKey,
            string displayName
        )
        {
            bool registered =
                RegisterHotKey(
                    _windowHandle,
                    id,
                    MOD_CONTROL |
                    MOD_SHIFT |
                    MOD_NOREPEAT,
                    virtualKey
                );

            if (registered)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"{displayName} registered successfully."
                );

                return;
            }

            int error =
                Marshal.GetLastWin32Error();

            System.Diagnostics.Debug.WriteLine(
                $"{displayName} registration failed. Win32 error: {error}"
            );

            System.Windows.MessageBox.Show(
                $"{displayName} could not be registered.\n\n" +
                "Another application may already be using this global shortcut.\n" +
                $"Windows error: {error}",
                "PrivateBrowser Hotkey Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }


        // =========================================================
        // WINDOWS MESSAGE HANDLER
        // =========================================================

        private IntPtr WndProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled
        )
        {
            if (
                msg !=
                WM_HOTKEY
            )
            {
                return IntPtr.Zero;
            }


            int hotkeyId =
                wParam.ToInt32();


            switch (
                hotkeyId
            )
            {
                case HOTKEY_LATEST_CAPTION:

                    CopyLatestCaption();

                    handled = true;

                    break;


                case HOTKEY_ALL_CAPTIONS:

                    CopyAllCaptions();

                    handled = true;

                    break;


                case HOTKEY_COPY_LATEST_AND_SEND:

                    handled = true;

                    Dispatcher.BeginInvoke(
                        new Action(
                            async () =>
                            {
                                await CopyLatestAndSendAsync();
                            }
                        )
                    );

                    break;


                case HOTKEY_PASTE_AND_SEND:

                    PasteClipboardAndSend();

                    handled = true;

                    break;

                case HOTKEY_TOGGLE_BROWSER:

                    TogglePrivateBrowser();

                    handled = true;

                    break;
            }


            return IntPtr.Zero;
        }


        // =========================================================
        // CTRL + SHIFT + C
        // COPY LATEST CAPTION
        // =========================================================

        private async void CopyLatestCaption()
        {
            try
            {
                CaptionItem? latestItem =
                    _captionStore.GetLatest();


                if (
                    latestItem == null
                )
                {
                    return;
                }


                string latest =
                    string.IsNullOrWhiteSpace(
                        latestItem.Speaker
                    )
                        ? latestItem.Text
                        : $"{latestItem.Speaker}: {latestItem.Text}";


                if (
                    string.IsNullOrWhiteSpace(
                        latest
                    )
                )
                {
                    return;
                }


                bool copied =
                    await TrySetClipboardTextAsync(
                        latest
                    );


                if (!copied)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Unable to copy latest caption."
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"CopyLatestCaption error: {ex}"
                );
            }
        }


        // =========================================================
        // CTRL + SHIFT + A
        // COPY FULL CURRENT MEETING TRANSCRIPT
        // =========================================================

        private async void CopyAllCaptions()
        {
            try
            {
                string transcript =
                    _captionStore.GetTranscript();


                if (
                    string.IsNullOrWhiteSpace(
                        transcript
                    )
                )
                {
                    return;
                }


                bool copied =
                    await TrySetClipboardTextAsync(
                        transcript
                    );


                if (!copied)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Unable to copy transcript."
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"CopyAllCaptions error: {ex}"
                );
            }
        }


        private async Task CopyLatestAndSendAsync()
        {
            try
            {
                CaptionItem? latestItem =
                    _captionStore.GetLatest();

                if (latestItem == null)
                {
                    System.Windows.MessageBox.Show(
                        "There is no latest caption to send.",
                        "PrivateBrowser",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    return;
                }

                string text =
                    string.IsNullOrWhiteSpace(
                        latestItem.Speaker
                    )
                        ? latestItem.Text
                        : $"{latestItem.Speaker}: {latestItem.Text}";

                if (string.IsNullOrWhiteSpace(text))
                {
                    System.Windows.MessageBox.Show(
                        "The latest caption is empty.",
                        "PrivateBrowser",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    return;
                }

                bool copied =
                    await TrySetClipboardTextAsync(
                        text
                    );

                if (!copied)
                {
                    System.Windows.MessageBox.Show(
                        "Could not copy the latest caption.",
                        "PrivateBrowser",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );

                    return;
                }

                await PasteIntoChatGptAndSendAsync(
                    text
                );
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Ctrl + Shift + S failed:\n\n{ex.Message}",
                    "PrivateBrowser",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }


        private async void PasteClipboardAndSend()
        {
            try
            {
                string text;

                try
                {
                    if (!WpfClipboard.ContainsText())
                    {
                        return;
                    }

                    text =
                        WpfClipboard.GetText();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Clipboard read failed: {ex}"
                    );

                    return;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                await PasteIntoChatGptAndSendAsync(
                    text
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"PasteClipboardAndSend error: {ex}"
                );
            }
        }


        private void TogglePrivateBrowser()
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        // Hidden by Ctrl+Shift+Q
                        if (_browserHiddenByHotkey)
                        {
                            RestorePrivateBrowserFromHotkey();
                            return;
                        }

                        // Hidden normally by Minimize / X
                        if (
                            !IsVisible ||
                            WindowState == WindowState.Minimized
                        )
                        {
                            ShowPrivateBrowser();
                            return;
                        }

                        // Currently visible → hide using the
                        // off-screen method used by the hotkey.
                        HidePrivateBrowserByHotkey();
                    }
                )
            );
        }


        // =========================================================
        // CLIPBOARD RETRY
        // =========================================================

        private async Task<bool>
            TrySetClipboardTextAsync(
                string text
            )
        {
            if (
                string.IsNullOrWhiteSpace(
                    text
                )
            )
            {
                return false;
            }


            const int maxAttempts =
                12;

            const int delayMs =
                100;


            for (
                int attempt = 1;
                attempt <= maxAttempts;
                attempt++
            )
            {
                try
                {
                    WpfClipboard.SetText(
                        text
                    );

                    return true;
                }
                catch (
                    COMException ex
                )
                when (
                    unchecked(
                        (uint)ex.HResult
                    )
                    ==
                    0x800401D0
                )
                {
                    await Task.Delay(
                        delayMs
                    );
                }
                catch (
                    Exception ex
                )
                {
                    System.Diagnostics.Debug
                        .WriteLine(
                            $"Clipboard error: {ex}"
                        );

                    return false;
                }
            }


            return false;
        }


        // =========================================================
        // CLEANUP
        // =========================================================

        private async void MainWindow_Closed(
            object? sender,
            EventArgs e
        )
        {
            _captionStore.Changed -=
                CaptionStore_Changed;


            if (
                _trayIcon !=
                null
            )
            {
                _trayIcon.Visible =
                    false;

                _trayIcon.Dispose();

                _trayIcon =
                    null;
            }


            if (
                _windowHandle !=
                IntPtr.Zero
            )
            {
                UnregisterHotKey(
                    _windowHandle,
                    HOTKEY_LATEST_CAPTION
                );

                UnregisterHotKey(
                    _windowHandle,
                    HOTKEY_ALL_CAPTIONS
                );

                UnregisterHotKey(
                    _windowHandle,
                    HOTKEY_COPY_LATEST_AND_SEND
                );

                UnregisterHotKey(
                    _windowHandle,
                    HOTKEY_PASTE_AND_SEND
                );
                UnregisterHotKey(
                    _windowHandle,
                    HOTKEY_TOGGLE_BROWSER
                );
            }


            if (
                _captionServer !=
                null
            )
            {
                try
                {
                    await _captionServer
                        .StopAsync();
                }
                catch (
                    Exception ex
                )
                {
                    System.Diagnostics.Debug
                        .WriteLine(
                            $"Caption server shutdown error: {ex}"
                        );
                }


                _captionServer =
                    null;
            }
        }

        // =========================================================
        // Caption Buttons
        // =========================================================
        private void CopyLatestButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            CopyLatestCaption();
        }

        private void CopyAllButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            CopyAllCaptions();
        }

        private void ClearCaptionsButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            _captionStore.Clear();
        }

        private async void PasteAndSendButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            try
            {
                if (
                    Browser.CoreWebView2 == null
                )
                {
                    return;
                }

                string text;

                try
                {
                    if (
                        !WpfClipboard.ContainsText()
                    )
                    {
                        System.Windows.MessageBox.Show(
                            "Clipboard does not contain text.",
                            "PrivateBrowser",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );

                        return;
                    }

                    text =
                        WpfClipboard.GetText();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Clipboard read failed: {ex}"
                    );

                    return;
                }

                if (
                    string.IsNullOrWhiteSpace(text)
                )
                {
                    return;
                }

                await PasteIntoChatGptAndSendAsync(
                    text
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Paste & Send failed: {ex}"
                );

                System.Windows.MessageBox.Show(
                    $"Paste & Send failed:\n\n{ex.Message}",
                    "PrivateBrowser",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private async Task PasteIntoChatGptAndSendAsync(
            string text
        )
        {
            if (
                Browser.CoreWebView2 == null
            )
            {
                return;
            }

            string currentUrl =
                Browser.Source?.ToString() ??
                string.Empty;

            if (
                !currentUrl.Contains(
                    "chatgpt.com",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                System.Windows.MessageBox.Show(
                    "Please open ChatGPT first.",
                    "PrivateBrowser",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                return;
            }

            // Safely encode C# text for JavaScript.
            string encodedText =
                System.Text.Json.JsonSerializer.Serialize(
                    text
                );

            string script =
                $$"""
                (() => {
                    const text = {{encodedText}};

                    // ---------------------------------------------
                    // FIND CHATGPT INPUT
                    // ---------------------------------------------

                    const input =
                        document.querySelector(
                            '#prompt-textarea'
                        ) ||
                        document.querySelector(
                            'div[contenteditable="true"]'
                        ) ||
                        document.querySelector(
                            'textarea'
                        );

                    if (!input) {
                        return JSON.stringify({
                            success: false,
                            reason: 'input-not-found'
                        });
                    }


                    // ---------------------------------------------
                    // FOCUS INPUT
                    // ---------------------------------------------

                    input.focus();


                    // ---------------------------------------------
                    // TEXTAREA CASE
                    // ---------------------------------------------

                    if (
                        input instanceof HTMLTextAreaElement ||
                        input instanceof HTMLInputElement
                    ) {
                        const prototype =
                            input instanceof HTMLTextAreaElement
                                ? HTMLTextAreaElement.prototype
                                : HTMLInputElement.prototype;

                        const descriptor =
                            Object.getOwnPropertyDescriptor(
                                prototype,
                                'value'
                            );

                        if (
                            descriptor &&
                            descriptor.set
                        ) {
                            descriptor.set.call(
                                input,
                                text
                            );
                        } else {
                            input.value =
                                text;
                        }

                        input.dispatchEvent(
                            new Event(
                                'input',
                                {
                                    bubbles: true
                                }
                            )
                        );
                    }

                    // ---------------------------------------------
                    // CONTENTEDITABLE CASE
                    // ---------------------------------------------

                    else {
                        input.textContent =
                            text;

                        input.dispatchEvent(
                            new InputEvent(
                                'input',
                                {
                                    bubbles: true,
                                    inputType: 'insertText',
                                    data: text
                                }
                            )
                        );
                    }


                    // ---------------------------------------------
                    // WAIT FOR CHATGPT UI TO UPDATE
                    // ---------------------------------------------

                    setTimeout(
                        () => {

                            // Prefer clicking the actual Send button.

                            const sendButton =
                                document.querySelector(
                                    'button[data-testid="send-button"]'
                                ) ||
                                document.querySelector(
                                    'button[aria-label="Send prompt"]'
                                ) ||
                                document.querySelector(
                                    'button[aria-label="Send message"]'
                                );

                            if (
                                sendButton &&
                                !sendButton.disabled
                            ) {
                                sendButton.click();

                                return;
                            }


                            // -------------------------------------
                            // FALLBACK: ENTER KEY
                            // -------------------------------------

                            input.dispatchEvent(
                                new KeyboardEvent(
                                    'keydown',
                                    {
                                        key: 'Enter',
                                        code: 'Enter',
                                        keyCode: 13,
                                        which: 13,
                                        bubbles: true,
                                        cancelable: true
                                    }
                                )
                            );

                        },
                        150
                    );


                    return JSON.stringify({
                        success: true
                    });
                })();
                """;

            string result =
                await Browser.CoreWebView2
                    .ExecuteScriptAsync(
                        script
                    );

            System.Diagnostics.Debug.WriteLine(
                $"Paste & Send result: {result}"
            );
        }

        // private void OpacitySlider_ValueChanged(
        //     object sender,
        //     RoutedPropertyChangedEventArgs<double> e
        // )
        // {
        //     double percent =
        //         e.NewValue;

        //     this.Opacity =
        //         Math.Clamp(
        //             percent / 100.0,
        //             0.10,
        //             1.0
        //         );

        //     if (
        //         OpacityValueText != null
        //     )
        //     {
        //         OpacityValueText.Text =
        //             $"{Math.Round(percent)}%";
        //     }
        // }
    }
}