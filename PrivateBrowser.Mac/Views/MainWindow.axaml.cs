using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PrivateBrowser.Mac.Services;

namespace PrivateBrowser.Mac.Views
{
    public partial class MainWindow : Window
    {
        private readonly CaptionStore _captionStore = new();
        private readonly LiveAudioBuffer _micBuffer = new();
        private readonly LiveAudioBuffer _systemBuffer = new();
        private readonly MacMicrophoneCapture _microphone = new();
        private readonly MacSystemAudioCapture _systemAudio = new();
        private readonly MacHotkeyService _hotkeys = new();
        private WhisperTranscriptionService? _whisper;
        private bool _browserHidden;
        private bool _screenshotBusy;
        private bool _captureHideActive;
        private PixelPoint _savedPosition;
        private bool _savedTopmost;

        public MainWindow()
        {
            InitializeComponent();

            Opened += MainWindow_Opened;
            Closed += MainWindow_Closed;
            _captionStore.Changed += CaptionStore_Changed;
        }

        private async void MainWindow_Opened(
            object? sender,
            EventArgs e
        )
        {
            MacWindowChrome.ApplyShareExclusion(this);

            AddressBar.Text = "https://chatgpt.com";
            Navigate(AddressBar.Text);

            try
            {
                string modelPath =
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Models",
                        "ggml-base.en.bin"
                    );

                _whisper =
                    new WhisperTranscriptionService(
                        modelPath
                    );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            _microphone.AudioAvailable += pcm =>
            {
                _micBuffer.Add(pcm);
                _ = ProcessBufferAsync(_micBuffer, "Me");
            };

            _systemAudio.AudioAvailable += pcm =>
            {
                _systemBuffer.Add(pcm);
                _ = ProcessBufferAsync(_systemBuffer, "Interviewer");
            };

            try
            {
                _microphone.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            _systemAudio.Start();
            _hotkeys.HotkeyPressed += OnHotkey;
            _hotkeys.Register();
            RenderTranscript(_captionStore.GetHistory());

            await Task.CompletedTask;
        }

        private void MainWindow_Closed(
            object? sender,
            EventArgs e
        )
        {
            _hotkeys.Dispose();
            _microphone.Dispose();
            _systemAudio.Stop();
            _whisper?.Dispose();
        }

        private void CaptionStore_Changed()
        {
            Dispatcher.UIThread.Post(
                () => RenderTranscript(
                    _captionStore.GetHistory()
                )
            );
        }

        private async Task ProcessBufferAsync(
            LiveAudioBuffer buffer,
            string speaker
        )
        {
            if (_whisper == null || !buffer.HasChunkReady())
            {
                return;
            }

            byte[] chunk = buffer.TakeChunk();
            if (chunk.Length == 0)
            {
                return;
            }

            try
            {
                string text =
                    await _whisper.TranscribeAsync(
                        chunk
                    );

                if (!string.IsNullOrWhiteSpace(text))
                {
                    _captionStore.Append(speaker, text);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void RenderTranscript(
            IReadOnlyList<CaptionItem> history
        )
        {
            TranscriptList.Children.Clear();

            if (history.Count == 0)
            {
                TranscriptList.Children.Add(
                    new TextBlock
                    {
                        Text = "Waiting for live captions...",
                        Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(4)
                    }
                );
                return;
            }

            foreach (CaptionItem item in history)
            {
                TranscriptList.Children.Add(
                    CreateTranscriptItem(item)
                );
            }

            TranscriptScroller.ScrollToEnd();
        }

        private Control CreateTranscriptItem(
            CaptionItem item
        )
        {
            string speaker =
                string.IsNullOrWhiteSpace(item.Speaker)
                    ? "Interviewer"
                    : item.Speaker;

            Button send = new()
            {
                Content = "Send",
                Width = 52,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0,
                Tag = item
            };

            send.Click += async (_, _) =>
            {
                await SendCaptionAsync(item);
            };

            Grid grid = new();
            StackPanel content = new()
            {
                Margin = new Thickness(0, 0, 58, 0)
            };
            content.Children.Add(
                new TextBlock
                {
                    Text = speaker,
                    FontWeight = FontWeight.Bold,
                    FontSize = 13
                }
            );
            content.Children.Add(
                new TextBlock
                {
                    Text = item.Text,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                }
            );
            grid.Children.Add(content);
            grid.Children.Add(send);

            Border border = new()
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Background = Brushes.White,
                Child = grid
            };

            border.PointerEntered += (_, _) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(232, 242, 255));
                send.Opacity = 1;
            };

            border.PointerExited += (_, _) =>
            {
                border.Background = Brushes.White;
                send.Opacity = 0;
            };

            return border;
        }

        private async void OnHotkey(
            MacHotkeyService.HotkeyAction action
        )
        {
            switch (action)
            {
                case MacHotkeyService.HotkeyAction.CopyLatest:
                    await CopyTextAsync(
                        InterviewPrompt.FormatCaptionLine(
                            _captionStore.GetLatest() ?? new CaptionItem()
                        )
                    );
                    break;

                case MacHotkeyService.HotkeyAction.CopyAll:
                    await CopyTextAsync(
                        _captionStore.GetTranscript()
                    );
                    break;

                case MacHotkeyService.HotkeyAction.CopyLatestAndSend:
                    await CopyLatestAndSendAsync();
                    break;

                case MacHotkeyService.HotkeyAction.PasteAndSend:
                    await PasteClipboardAndSendAsync();
                    break;

                case MacHotkeyService.HotkeyAction.ToggleWindow:
                    ToggleHidden();
                    break;

                case MacHotkeyService.HotkeyAction.Screenshot:
                    await CaptureScreenshotAndAttachToChatGptAsync();
                    break;
            }
        }

        private void ToggleHidden()
        {
            _browserHidden = !_browserHidden;
            ShowInTaskbar = !_browserHidden;
            WindowState = _browserHidden
                ? WindowState.Minimized
                : WindowState.Normal;

            if (!_browserHidden)
            {
                Activate();
            }
        }

        private async Task CopyLatestAndSendAsync()
        {
            string recent =
                _captionStore.GetRecentTranscript(
                    InterviewPrompt.RecentTranscriptBlockCount
                );

            if (string.IsNullOrWhiteSpace(recent))
            {
                return;
            }

            string text =
                InterviewPrompt.Build(recent);

            await CopyTextAsync(text);
            await PasteIntoChatGptAndSendAsync(text);
        }

        private async Task SendCaptionAsync(
            CaptionItem item
        )
        {
            string text =
                InterviewPrompt.FormatCaptionLine(item);

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            await CopyTextAsync(text);
            await PasteIntoChatGptAndSendAsync(text);
        }

        private async Task PasteClipboardAndSendAsync()
        {
            var clipboard =
                GetTopLevel(this)?.Clipboard;

            if (clipboard == null)
            {
                return;
            }

            string? text =
                await clipboard.GetTextAsync();

            if (!string.IsNullOrWhiteSpace(text))
            {
                await PasteIntoChatGptAndSendAsync(text);
            }
        }

        private async Task PasteIntoChatGptAndSendAsync(
            string text,
            bool send = true,
            bool replaceExisting = true
        )
        {
            string url = GetChatGptUrl();

            if (!url.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Activate();
            await Task.Delay(80);

            string script =
                ChatGptPasteService.BuildInsertAndSendScript(
                    text,
                    send,
                    replaceExisting
                );

            try
            {
                await Browser.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private async void ScreenshotButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            await CaptureScreenshotAndAttachToChatGptAsync();
        }

        private async Task CaptureScreenshotAndAttachToChatGptAsync()
        {
            if (_screenshotBusy)
            {
                return;
            }

            _screenshotBusy = true;
            string? tempPath = null;

            try
            {
                string url = GetChatGptUrl();

                if (!url.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                bool hidForCapture = false;

                if (!_browserHidden && !_captureHideActive)
                {
                    HideForScreenshot();
                    hidForCapture = true;
                    await Task.Delay(250);
                }

                try
                {
                    tempPath =
                        MacScreenshotCapture.CaptureMainDisplayPng();
                }
                finally
                {
                    if (hidForCapture)
                    {
                        RestoreFromScreenshot();
                    }
                }

                if (string.IsNullOrWhiteSpace(tempPath) ||
                    !File.Exists(tempPath))
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Screenshot capture failed. Grant Screen Recording in System Settings."
                    );
                    return;
                }

                MacScreenshotCapture.CopyPngToPasteboard(tempPath);

                if (_browserHidden)
                {
                    ToggleHidden();
                }

                Activate();
                await Task.Delay(120);

                bool attached = false;
                FileInfo pngInfo = new(tempPath);
                if (pngInfo.Length < 1_500_000)
                {
                    attached =
                        await AttachScreenshotFileToChatGptAsync(tempPath);
                }

                if (!attached)
                {
                    await FocusChatGptComposerAsync();
                    await Task.Delay(80);
                    MacScreenshotCapture.SendCommandV();
                    await Task.Delay(450);
                }
                else
                {
                    await Task.Delay(350);
                }

                await PasteIntoChatGptAndSendAsync(
                    ChatGptPasteService.ScreenshotAnalysisPrompt,
                    send: false,
                    replaceExisting: false
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Screenshot capture failed: {ex}"
                );
            }
            finally
            {
                _screenshotBusy = false;

                if (!string.IsNullOrWhiteSpace(tempPath))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(8000);
                            if (File.Exists(tempPath))
                            {
                                File.Delete(tempPath);
                            }
                        }
                        catch
                        {
                        }
                    });
                }
            }
        }

        private void HideForScreenshot()
        {
            if (_captureHideActive)
            {
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            _savedPosition = Position;
            _savedTopmost = Topmost;
            _captureHideActive = true;
            Topmost = false;
            Position = GetOffscreenPosition();
        }

        private void RestoreFromScreenshot()
        {
            if (!_captureHideActive)
            {
                return;
            }

            Position = _savedPosition;
            Topmost = _savedTopmost;
            _captureHideActive = false;
            WindowState = WindowState.Normal;
            Activate();
        }

        private async Task<bool> AttachScreenshotFileToChatGptAsync(
            string pngPath
        )
        {
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(pngPath);
                string base64 = Convert.ToBase64String(bytes);
                string script =
                    ChatGptPasteService.BuildAttachPngScript(base64);

                string? result =
                    await Browser.ExecuteScriptAsync(script);

                return !string.IsNullOrWhiteSpace(result) &&
                    result.Contains("true", StringComparison.OrdinalIgnoreCase) &&
                    !result.Contains("false", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ChatGPT screenshot attach failed: {ex}"
                );

                return false;
            }
        }

        private async Task FocusChatGptComposerAsync()
        {
            try
            {
                await Browser.ExecuteScriptAsync(
                    ChatGptPasteService.BuildFocusComposerScript()
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private string GetChatGptUrl()
        {
            return Browser.Url?.ToString() ??
                AddressBar.Text ??
                string.Empty;
        }

        private PixelPoint GetOffscreenPosition()
        {
            int minX = 0;

            if (Screens != null)
            {
                foreach (var screen in Screens.All)
                {
                    minX = Math.Min(minX, screen.Bounds.X);
                }
            }

            int width =
                (int)Math.Max(
                    Bounds.Width > 0 ? Bounds.Width : Width,
                    800
                );

            return new PixelPoint(
                minX - width - 300,
                Position.Y
            );
        }

        private async Task CopyTextAsync(
            string text
        )
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
            }
        }

        private void Navigate(
            string address
        )
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            address = address.Trim();

            if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !address.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
            {
                address = "https://" + address;
            }

            AddressBar.Text = address;
            Browser.Url = new Uri(address);
        }

        private void GoButton_Click(object? sender, RoutedEventArgs e)
        {
            Navigate(AddressBar.Text ?? string.Empty);
        }

        private void AddressBar_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Navigate(AddressBar.Text ?? string.Empty);
            }
        }

        private void BackButton_Click(object? sender, RoutedEventArgs e)
        {
            if (Browser.IsCanGoBack)
            {
                Browser.GoBack();
            }
        }

        private void ForwardButton_Click(object? sender, RoutedEventArgs e)
        {
            if (Browser.IsCanGoForward)
            {
                Browser.GoForward();
            }
        }

        private void RefreshButton_Click(object? sender, RoutedEventArgs e)
        {
            Browser.Reload();
        }

        private async void CopyLatestButton_Click(object? sender, RoutedEventArgs e)
        {
            await CopyTextAsync(
                InterviewPrompt.FormatCaptionLine(
                    _captionStore.GetLatest() ?? new CaptionItem()
                )
            );
        }

        private async void CopyAllButton_Click(object? sender, RoutedEventArgs e)
        {
            await CopyTextAsync(_captionStore.GetTranscript());
        }

        private async void PasteAndSendButton_Click(object? sender, RoutedEventArgs e)
        {
            await PasteClipboardAndSendAsync();
        }

        private void ClearButton_Click(object? sender, RoutedEventArgs e)
        {
            _micBuffer.Clear();
            _systemBuffer.Clear();
            _captionStore.Clear();
        }
    }
}
