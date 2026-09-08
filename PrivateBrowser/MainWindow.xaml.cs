using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text;
using System.Text.Json;

using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

// Explicit WPF aliases prevent ambiguity after <UseWindowsForms>true</UseWindowsForms>.
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.DataObject;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfKey = System.Windows.Input.Key;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace PrivateBrowser
{
    public partial class MainWindow : Window
    {
        // =========================================================
        // WINDOW HANDLE
        // =========================================================

        private IntPtr _windowHandle = IntPtr.Zero;

        private BrowserOverlayWindow _browserOverlay = null!;

        private WebView2 Browser =>
            _browserOverlay.WebViewControl;

        private bool _overlaySyncQueued;

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


        private const int RecentTranscriptBlockCount = 3;

        private static readonly WpfSolidColorBrush TranscriptItemBackground =
            new WpfSolidColorBrush(
                WpfColor.FromRgb(255, 255, 255)
            );

        private static readonly WpfSolidColorBrush TranscriptItemHoverBackground =
            new WpfSolidColorBrush(
                WpfColor.FromRgb(232, 242, 255)
            );

        private static readonly WpfSolidColorBrush TranscriptItemBorderBrush =
            new WpfSolidColorBrush(
                WpfColor.FromRgb(230, 230, 230)
            );

        private static readonly WpfSolidColorBrush TranscriptItemHoverBorderBrush =
            new WpfSolidColorBrush(
                WpfColor.FromRgb(170, 200, 235)
            );

        private static readonly WpfSolidColorBrush TranscriptMutedForeground =
            new WpfSolidColorBrush(
                WpfColor.FromRgb(120, 120, 120)
            );

        private static readonly WpfSolidColorBrush TranscriptBodyForeground =
            new WpfSolidColorBrush(
                WpfColor.FromRgb(38, 38, 38)
            );

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
            history ??=
                Array.Empty<CaptionItem>();


            bool showingEmptyPlaceholder =
                TranscriptList.Children.Count == 1
                &&
                TranscriptList.Children[0] is TextBlock;


            if (history.Count == 0)
            {
                if (!showingEmptyPlaceholder)
                {
                    TranscriptList.Children.Clear();


                    TranscriptList.Children.Add(
                        CreateEmptyTranscriptPlaceholder()
                    );
                }


                return;
            }


            if (showingEmptyPlaceholder)
            {
                TranscriptList.Children.Clear();
            }


            while (
                TranscriptList.Children.Count >
                history.Count
            )
            {
                TranscriptList.Children.RemoveAt(
                    TranscriptList.Children.Count - 1
                );
            }


            for (
                int i = 0;
                i < history.Count;
                i++
            )
            {
                CaptionItem item =
                    history[i];


                if (
                    i < TranscriptList.Children.Count
                    &&
                    TranscriptList.Children[i] is Border existing
                    &&
                    ReferenceEquals(
                        existing.Tag,
                        item
                    )
                )
                {
                    UpdateTranscriptItem(
                        existing,
                        item
                    );

                    continue;
                }


                UIElement created =
                    CreateTranscriptItem(
                        item
                    );


                if (
                    i < TranscriptList.Children.Count
                )
                {
                    TranscriptList.Children.RemoveAt(
                        i
                    );

                    TranscriptList.Children.Insert(
                        i,
                        created
                    );
                }
                else
                {
                    TranscriptList.Children.Add(
                        created
                    );
                }
            }


            TranscriptScroller.ScrollToEnd();
        }


        private TextBlock CreateEmptyTranscriptPlaceholder()
        {
            return new TextBlock
            {
                Text =
                    "Waiting for live captions...",
                Foreground =
                    TranscriptMutedForeground,
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        4
                    )
            };
        }


        private void UpdateTranscriptItem(
            Border border,
            CaptionItem item
        )
        {
            string speaker =
                string.IsNullOrWhiteSpace(
                    item.Speaker
                )
                    ? "Interviewer"
                    : item.Speaker;


            if (
                border.Child is not Grid grid
            )
            {
                return;
            }


            foreach (
                UIElement child
                in grid.Children
            )
            {
                if (
                    child is StackPanel panel
                    &&
                    panel.Children.Count >= 2
                )
                {
                    if (
                        panel.Children[0] is TextBlock speakerBlock
                    )
                    {
                        speakerBlock.Text =
                            speaker;

                        speakerBlock.Foreground =
                            GetSpeakerBrush(
                                speaker
                            );
                    }


                    if (
                        panel.Children[1] is TextBlock textBlock
                    )
                    {
                        textBlock.Text =
                            item.Text;
                    }
                }


                if (
                    child is WpfButton sendButton
                )
                {
                    sendButton.Tag =
                        item;
                }
            }


            border.Tag =
                item;
        }


        private UIElement CreateTranscriptItem(
            CaptionItem item
        )
        {
            string speaker =
                string.IsNullOrWhiteSpace(
                    item.Speaker
                )
                    ? "Interviewer"
                    : item.Speaker;


            Border border =
                new Border
                {
                    Background =
                        TranscriptItemBackground,
                    BorderBrush =
                        TranscriptItemBorderBrush,
                    BorderThickness =
                        new Thickness(
                            1
                        ),
                    CornerRadius =
                        new CornerRadius(
                            6
                        ),
                    Padding =
                        new Thickness(
                            8
                        ),
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            8
                        ),
                    Tag =
                        item
                };


            Grid grid =
                new Grid();


            StackPanel content =
                new StackPanel
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            58,
                            0
                        )
                };


            TextBlock speakerBlock =
                new TextBlock
                {
                    Text =
                        speaker,
                    FontWeight =
                        FontWeights.Bold,
                    FontSize =
                        13,
                    Foreground =
                        GetSpeakerBrush(
                            speaker
                        ),
                    TextWrapping =
                        TextWrapping.Wrap
                };


            TextBlock textBlock =
                new TextBlock
                {
                    Text =
                        item.Text,
                    FontWeight =
                        FontWeights.Normal,
                    FontSize =
                        14,
                    Foreground =
                        TranscriptBodyForeground,
                    TextWrapping =
                        TextWrapping.Wrap,
                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            0
                        )
                };


            content.Children.Add(
                speakerBlock
            );

            content.Children.Add(
                textBlock
            );


            WpfButton sendButton =
                new WpfButton
                {
                    Content =
                        "Send",
                    Width =
                        52,
                    Height =
                        24,
                    FontSize =
                        11,
                    Padding =
                        new Thickness(
                            0
                        ),
                    HorizontalAlignment =
                        WpfHorizontalAlignment.Right,
                    VerticalAlignment =
                        WpfVerticalAlignment.Top,
                    Opacity =
                        0,
                    IsHitTestVisible =
                        false,
                    Cursor =
                        WpfCursors.Hand,
                    ToolTip =
                        "Copy this caption and send it to ChatGPT",
                    Tag =
                        item
                };


            sendButton.Click +=
                TranscriptSendButton_Click;


            grid.Children.Add(
                content
            );

            grid.Children.Add(
                sendButton
            );


            border.Child =
                grid;


            border.MouseEnter +=
                (sender, e) =>
                {
                    border.Background =
                        TranscriptItemHoverBackground;

                    border.BorderBrush =
                        TranscriptItemHoverBorderBrush;

                    sendButton.Opacity =
                        1;

                    sendButton.IsHitTestVisible =
                        true;
                };


            border.MouseLeave +=
                (sender, e) =>
                {
                    border.Background =
                        TranscriptItemBackground;

                    border.BorderBrush =
                        TranscriptItemBorderBrush;

                    sendButton.Opacity =
                        0;

                    sendButton.IsHitTestVisible =
                        false;
                };


            return border;
        }


        private static string FormatCaptionLine(
            CaptionItem item
        )
        {
            if (
                item == null ||
                string.IsNullOrWhiteSpace(
                    item.Text
                )
            )
            {
                return string.Empty;
            }


            return string.IsNullOrWhiteSpace(
                item.Speaker
            )
                ? item.Text
                : $"{item.Speaker}: {item.Text}";
        }


        private static string BuildInterviewAssistPrompt(
            string transcript
        )
        {
            StringBuilder builder =
                new StringBuilder();


            builder.AppendLine(
                "You are assisting a candidate during a live interview."
            );

            builder.AppendLine();

            builder.AppendLine(
                "The excerpts below are the most recent spoken blocks from the conversation. Use them as context, then answer the interviewer's latest question as the candidate would in the interview."
            );

            builder.AppendLine();

            builder.AppendLine(
                "Instructions:"
            );

            builder.AppendLine(
                "1. Identify the interviewer's most recent question from the transcript."
            );

            builder.AppendLine(
                "2. Provide a clear, concise, interview-ready spoken answer to that question."
            );

            builder.AppendLine(
                "3. Use earlier excerpts only as supporting context."
            );

            builder.AppendLine(
                "4. Do not recap or quote the transcript unless a short reference is necessary."
            );

            builder.AppendLine(
                "5. If the last block is from Me (the candidate), ignore it. Answer the interviewer's most recent question from the remaining transcript."
            );

            builder.AppendLine();

            builder.AppendLine(
                "Transcript:"
            );

            builder.AppendLine(
                "-----"
            );

            builder.AppendLine(
                transcript.Trim()
            );

            builder.Append(
                "-----"
            );


            return builder.ToString();
        }

        // =========================================================
        // CAPTION STORE + SERVER
        // =========================================================

        private readonly CaptionStore _captionStore =
            new CaptionStore();

        private readonly SystemAudioCaptureService _systemAudioCapture =
            new SystemAudioCaptureService();

        private WhisperTranscriptionService? _whisperService;

        private readonly object _interviewerWhisperQueueLock =
            new object();

        private byte[]? _pendingInterviewerAudio;

        private bool _interviewerWhisperProcessing =
            false;

        private readonly object _microphoneWhisperQueueLock =
            new object();

        private byte[]? _pendingMicrophoneAudio;

        private bool _microphoneWhisperProcessing =
            false;

        private bool _whisperInitialized = false;

        private VoiceActivityService? _voiceActivityService;

        private readonly MicrophoneCaptureService _microphoneCapture =
            new MicrophoneCaptureService();

        private readonly LiveAudioBuffer _microphoneLiveAudioBuffer =
                new LiveAudioBuffer();

        private DateTime _lastMicrophoneSpeechTime =
            DateTime.MinValue;

        private bool _microphoneFinalizeScheduled =
            false;

        private DateTime _lastSpeechTime =
            DateTime.MinValue;

        private readonly TimeSpan _speechEndDelay =
            TimeSpan.FromMilliseconds(
                900
            );

        private bool _finalizeScheduled =
            false;

        private readonly LiveAudioBuffer _liveAudioBuffer =
            new LiveAudioBuffer();

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
                IReadOnlyList<CaptionItem> history =
                    _captionStore.GetHistory();

                RenderTranscript(
                    history
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
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

        private void SystemAudioCapture_AudioAvailable(
            byte[] buffer,
            NAudio.Wave.WaveFormat format
        )
        {
            try
            {
                byte[] converted =
                    AudioConverter.To16KhzMonoPcm16(
                        buffer,
                        format
                    );


                double rms =
                    CalculateRms(
                        converted
                    );


                const double silenceThreshold =
                    0.008;


                bool containsAudio =
                    rms >= silenceThreshold;

                if (containsAudio)
                {
                    _lastSpeechTime =
                        DateTime.UtcNow;
                }
                else
                {
                    ScheduleLiveCaptionFinalization();

                    return;
                }


                _lastSpeechTime =
                    DateTime.UtcNow;


                _liveAudioBuffer.Add(
                    converted
                );

                // =====================================================
                // EARLY LIVE PREVIEW
                // =====================================================

                if (
                    _liveAudioBuffer.HasPreviewReady()
                )
                {
                    byte[] preview =
                        _liveAudioBuffer.TakePreview();

                    if (
                        preview.Length > 0
                    )
                    {
                        QueueInterviewerWhisper(
                            preview
                        );
                    }
                }


                if (
                    !_liveAudioBuffer.HasChunkReady()
                )
                {
                    return;
                }


                byte[] chunk =
                    _liveAudioBuffer.TakeChunk();


                if (
                    chunk.Length == 0
                )
                {
                    return;
                }


                QueueInterviewerWhisper(
                    chunk
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Live audio processing failed: {ex}"
                );
            }
        }

        private void QueueInterviewerWhisper(
    byte[] audio
)
        {
            if (
                audio == null ||
                audio.Length == 0
            )
            {
                return;
            }


            bool shouldStart =
                false;


            lock (_interviewerWhisperQueueLock)
            {
                // Keep the newest waiting chunk.
                //
                // If Whisper is slower than incoming audio,
                // we don't build a huge delayed queue.
                _pendingInterviewerAudio =
                    audio;


                if (!_interviewerWhisperProcessing)
                {
                    _interviewerWhisperProcessing =
                        true;

                    shouldStart =
                        true;
                }
            }


            if (shouldStart)
            {
                _ =
                    ProcessInterviewerWhisperQueueAsync();
            }
        }

        private async Task ProcessInterviewerWhisperQueueAsync()
        {
            while (true)
            {
                byte[]? audio;


                lock (_interviewerWhisperQueueLock)
                {
                    audio =
                        _pendingInterviewerAudio;


                    _pendingInterviewerAudio =
                        null;


                    if (audio == null)
                    {
                        _interviewerWhisperProcessing =
                            false;

                        return;
                    }
                }


                await TranscribeLiveWhisperAsync(
                    audio
                );
            }
        }

        private async Task TranscribeLiveWhisperAsync(
            byte[] audio
        )
        {
            if (
                _whisperService == null ||
                audio == null ||
                audio.Length == 0
            )
            {
                return;
            }

            try
            {
                if (_voiceActivityService != null)
                {
                    bool containsSpeech =
                        await _voiceActivityService
                            .ContainsSpeechAsync(
                                audio
                            );

                    if (!containsSpeech)
                    {
                        return;
                    }
                }

                string text =
                    await _whisperService
                        .TranscribeAsync(
                            audio
                        );

                text =
                    CleanWhisperText(
                        text
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        text
                    )
                )
                {
                    return;
                }

                text =
                    text.Trim();

                CaptionItem? latest =
                    _captionStore.GetLatest();

                bool sameSpeaker =
                    latest != null
                    &&
                    string.Equals(
                        latest.Speaker,
                        "Interviewer",
                        StringComparison.OrdinalIgnoreCase
                    );

                string existingText =
                    sameSpeaker
                        ? latest!.Text
                        : string.Empty;

                string mergedText =
                    MergeWhisperCaption(
                        existingText,
                        text
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        mergedText
                    )
                )
                {
                    return;
                }

                if (
                    sameSpeaker
                    &&
                    string.Equals(
                        latest!.Text,
                        mergedText,
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }

                if (
                    sameSpeaker
                    &&
                    !string.IsNullOrWhiteSpace(
                        latest!.SegmentId
                    )
                )
                {
                    CaptionItem updatedCaption =
                        new CaptionItem
                        {
                            Speaker =
                                "Interviewer",

                            Text =
                                mergedText,

                            Timestamp =
                                DateTime.Now.ToString(
                                    "HH:mm:ss"
                                ),

                            MeetingKey =
                                null,

                            SegmentId =
                                latest.SegmentId
                        };

                    _captionStore.Add(
                        updatedCaption
                    );
                }
                else
                {
                    CaptionItem newCaption =
                        new CaptionItem
                        {
                            Speaker =
                                "Interviewer",

                            Text =
                                mergedText,

                            Timestamp =
                                DateTime.Now.ToString(
                                    "HH:mm:ss"
                                ),

                            MeetingKey =
                                null,

                            SegmentId =
                                Guid.NewGuid()
                                    .ToString("N")
                        };

                    _captionStore.Add(
                        newCaption
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Live Whisper failed: {ex}"
                );
            }
        }

        private static string MergeWhisperCaption(
            string existingText,
            string incomingText
        )
        {
            existingText =
                existingText?.Trim() ??
                string.Empty;

            incomingText =
                incomingText?.Trim() ??
                string.Empty;

            if (
                string.IsNullOrWhiteSpace(
                    incomingText
                )
            )
            {
                return existingText;
            }

            if (
                string.IsNullOrWhiteSpace(
                    existingText
                )
            )
            {
                return incomingText;
            }

            if (
                string.Equals(
                    existingText,
                    incomingText,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return existingText;
            }

            string[] existingWords =
                existingText.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );

            string[] incomingWords =
                incomingText.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );

            // Older text is protected. Only the recent tail may be revised.
            const int editableTailWords =
                20;

            int protectedCount =
                Math.Max(
                    0,
                    existingWords.Length -
                    editableTailWords
                );

            string[] protectedWords =
                existingWords
                    .Take(
                        protectedCount
                    )
                    .ToArray();

            string[] recentWords =
                existingWords
                    .Skip(
                        protectedCount
                    )
                    .ToArray();

            int bestStart =
                -1;

            int bestMatches =
                0;

            double bestSimilarity =
                0.0;

            for (
                int start = 0;
                start < recentWords.Length;
                start++
            )
            {
                int compareCount =
                    Math.Min(
                        recentWords.Length - start,
                        incomingWords.Length
                    );

                if (compareCount < 3)
                {
                    continue;
                }

                int matches =
                    0;

                for (
                    int i = 0;
                    i < compareCount;
                    i++
                )
                {
                    string oldWord =
                        NormalizeWhisperWord(
                            recentWords[
                                start + i
                            ]
                        );

                    string newWord =
                        NormalizeWhisperWord(
                            incomingWords[i]
                        );

                    if (
                        string.Equals(
                            oldWord,
                            newWord,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        matches++;
                    }
                }

                double similarity =
                    (double)matches /
                    compareCount;

                if (
                    similarity >
                    bestSimilarity
                )
                {
                    bestSimilarity =
                        similarity;

                    bestMatches =
                        matches;

                    bestStart =
                        start;
                }
            }

            // New Whisper output is probably a corrected version
            // of the recent unstable tail.
            if (
                bestStart >= 0
                &&
                bestMatches >= 3
                &&
                bestSimilarity >= 0.55
            )
            {
                string stablePrefix =
                    string.Join(
                        " ",
                        protectedWords
                            .Concat(
                                recentWords.Take(
                                    bestStart
                                )
                            )
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        stablePrefix
                    )
                )
                {
                    return incomingText;
                }

                return (
                    stablePrefix +
                    " " +
                    incomingText
                ).Trim();
            }

            // Exact suffix-prefix overlap fallback.
            int maximumOverlap =
                Math.Min(
                    existingWords.Length,
                    incomingWords.Length
                );

            maximumOverlap =
                Math.Min(
                    maximumOverlap,
                    25
                );

            for (
                int overlap = maximumOverlap;
                overlap >= 1;
                overlap--
            )
            {
                bool matches =
                    true;

                for (
                    int i = 0;
                    i < overlap;
                    i++
                )
                {
                    string oldWord =
                        NormalizeWhisperWord(
                            existingWords[
                                existingWords.Length -
                                overlap +
                                i
                            ]
                        );

                    string newWord =
                        NormalizeWhisperWord(
                            incomingWords[i]
                        );

                    if (
                        !string.Equals(
                            oldWord,
                            newWord,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        matches =
                            false;

                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                string additional =
                    string.Join(
                        " ",
                        incomingWords.Skip(
                            overlap
                        )
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        additional
                    )
                )
                {
                    return existingText;
                }

                return (
                    existingText +
                    " " +
                    additional
                ).Trim();
            }

            // Never throw away old stable text.
            return (
                existingText +
                " " +
                incomingText
            ).Trim();
        }



        // private static string MergeWhisperChunk(
        //     string existingText,
        //     string incomingText
        // )
        // {
        //     existingText =
        //         existingText?.Trim() ??
        //         string.Empty;

        //     incomingText =
        //         incomingText?.Trim() ??
        //         string.Empty;


        //     if (
        //         string.IsNullOrWhiteSpace(
        //             incomingText
        //         )
        //     )
        //     {
        //         return existingText;
        //     }


        //     if (
        //         string.IsNullOrWhiteSpace(
        //             existingText
        //         )
        //     )
        //     {
        //         return incomingText;
        //     }


        //     if (
        //         string.Equals(
        //             existingText,
        //             incomingText,
        //             StringComparison.OrdinalIgnoreCase
        //         )
        //     )
        //     {
        //         return existingText;
        //     }


        //     string[] existingWords =
        //         existingText.Split(
        //             ' ',
        //             StringSplitOptions.RemoveEmptyEntries
        //         );

        //     string[] incomingWords =
        //         incomingText.Split(
        //             ' ',
        //             StringSplitOptions.RemoveEmptyEntries
        //         );


        //     // =========================================================
        //     // IMPORTANT:
        //     //
        //     // Never allow Whisper to rewrite the entire transcript.
        //     // Only the most recent words are allowed to change.
        //     // =========================================================

        //     const int editableTailWords =
        //         18;


        //     int protectedWordCount =
        //         Math.Max(
        //             0,
        //             existingWords.Length -
        //             editableTailWords
        //         );


        //     // Everything before this point is considered permanent.
        //     string protectedPrefix =
        //         string.Join(
        //             " ",
        //             existingWords.Take(
        //                 protectedWordCount
        //             )
        //         );


        //     string[] editableTail =
        //         existingWords
        //             .Skip(
        //                 protectedWordCount
        //             )
        //             .ToArray();


        //     // =========================================================
        //     // FIND OVERLAP BETWEEN RECENT OLD TEXT AND NEW WHISPER TEXT
        //     // =========================================================

        //     int bestOldIndex =
        //         -1;

        //     int bestMatchLength =
        //         0;


        //     for (
        //         int start = 0;
        //         start < editableTail.Length;
        //         start++
        //     )
        //     {
        //         int matchLength =
        //             0;


        //         while (
        //             start + matchLength <
        //             editableTail.Length
        //             &&
        //             matchLength <
        //             incomingWords.Length
        //         )
        //         {
        //             string oldWord =
        //                 NormalizeWhisperWord(
        //                     editableTail[
        //                         start +
        //                         matchLength
        //                     ]
        //                 );


        //             string newWord =
        //                 NormalizeWhisperWord(
        //                     incomingWords[
        //                         matchLength
        //                     ]
        //                 );


        //             if (
        //                 !string.Equals(
        //                     oldWord,
        //                     newWord,
        //                     StringComparison.OrdinalIgnoreCase
        //                 )
        //             )
        //             {
        //                 break;
        //             }


        //             matchLength++;
        //         }


        //         if (
        //             matchLength >
        //             bestMatchLength
        //         )
        //         {
        //             bestMatchLength =
        //                 matchLength;

        //             bestOldIndex =
        //                 start;
        //         }
        //     }


        //     // =========================================================
        //     // WHISPER IS REVISING THE RECENT TAIL
        //     // =========================================================

        //     if (
        //         bestOldIndex >= 0 &&
        //         bestMatchLength >= 3
        //     )
        //     {
        //         string tailBeforeMatch =
        //             string.Join(
        //                 " ",
        //                 editableTail.Take(
        //                     bestOldIndex
        //                 )
        //             );


        //         string result =
        //             string.Join(
        //                 " ",
        //                 new[]
        //                 {
        //             protectedPrefix,
        //             tailBeforeMatch,
        //             incomingText
        //                 }
        //                 .Where(
        //                     value =>
        //                         !string.IsNullOrWhiteSpace(
        //                             value
        //                         )
        //                 )
        //             );


        //         return result.Trim();
        //     }


        //     // =========================================================
        //     // NORMAL SUFFIX/PREFIX OVERLAP
        //     // =========================================================

        //     int maximumOverlap =
        //         Math.Min(
        //             editableTail.Length,
        //             incomingWords.Length
        //         );


        //     for (
        //         int overlap = maximumOverlap;
        //         overlap >= 1;
        //         overlap--
        //     )
        //     {
        //         bool matches =
        //             true;


        //         for (
        //             int i = 0;
        //             i < overlap;
        //             i++
        //         )
        //         {
        //             string oldWord =
        //                 NormalizeWhisperWord(
        //                     editableTail[
        //                         editableTail.Length -
        //                         overlap +
        //                         i
        //                     ]
        //                 );


        //             string newWord =
        //                 NormalizeWhisperWord(
        //                     incomingWords[i]
        //                 );


        //             if (
        //                 !string.Equals(
        //                     oldWord,
        //                     newWord,
        //                     StringComparison.OrdinalIgnoreCase
        //                 )
        //             )
        //             {
        //                 matches =
        //                     false;

        //                 break;
        //             }
        //         }


        //         if (!matches)
        //         {
        //             continue;
        //         }


        //         string newPart =
        //             string.Join(
        //                 " ",
        //                 incomingWords.Skip(
        //                     overlap
        //                 )
        //             );


        //         if (
        //             string.IsNullOrWhiteSpace(
        //                 newPart
        //             )
        //         )
        //         {
        //             return existingText;
        //         }


        //         return (
        //             existingText +
        //             " " +
        //             newPart
        //         ).Trim();
        //     }


        //     // =========================================================
        //     // NO OVERLAP FOUND
        //     //
        //     // Do NOT replace old text.
        //     // Treat incoming speech as continuation.
        //     // =========================================================

        //     return (
        //         existingText +
        //         " " +
        //         incomingText
        //     ).Trim();
        // }


        private static string NormalizeWhisperWord(
            string word
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    word
                )
            )
            {
                return string.Empty;
            }


            return word
                .Trim()
                .Trim(
                    '.',
                    ',',
                    '!',
                    '?',
                    ':',
                    ';',
                    '"',
                    '\'',
                    '(',
                    ')',
                    '[',
                    ']'
                );
        }


        private void ScheduleLiveCaptionFinalization()
        {
            if (_finalizeScheduled)
            {
                return;
            }

            _finalizeScheduled =
                true;

            _ =
                FinalizeAfterSilenceAsync();
        }


        private async Task FinalizeAfterSilenceAsync()
        {
            try
            {
                await Task.Delay(
                    _speechEndDelay
                );

                TimeSpan silenceDuration =
                    DateTime.UtcNow -
                    _lastSpeechTime;

                if (
                    silenceDuration <
                    _speechEndDelay
                )
                {
                    return;
                }

                byte[] remainingAudio =
                    _liveAudioBuffer.TakeRemaining();

                if (
                    remainingAudio.Length > 0 &&
                    _whisperService != null
                )
                {
                    string text =
                        await _whisperService
                            .TranscribeAsync(
                                remainingAudio
                            );

                    text =
                        CleanWhisperText(
                            text
                        );

                    if (
                        !string.IsNullOrWhiteSpace(
                            text
                        )
                    )
                    {
                        text =
                            text.Trim();

                        CaptionItem? latest =
                            _captionStore.GetLatest();

                        bool sameSpeaker =
                            latest != null
                            &&
                            string.Equals(
                                latest.Speaker,
                                "Interviewer",
                                StringComparison.OrdinalIgnoreCase
                            );

                        string existingText =
                            sameSpeaker
                                ? latest!.Text
                                : string.Empty;

                        string mergedText =
                            MergeWhisperCaption(
                                existingText,
                                text
                            );

                        if (
                            !string.IsNullOrWhiteSpace(
                                mergedText
                            )
                        )
                        {
                            if (
                                sameSpeaker
                                &&
                                !string.IsNullOrWhiteSpace(
                                    latest!.SegmentId
                                )
                            )
                            {
                                if (
                                    !string.Equals(
                                        latest.Text,
                                        mergedText,
                                        StringComparison.Ordinal
                                    )
                                )
                                {
                                    CaptionItem updatedCaption =
                                        new CaptionItem
                                        {
                                            Speaker =
                                                "Interviewer",

                                            Text =
                                                mergedText,

                                            Timestamp =
                                                DateTime.Now.ToString(
                                                    "HH:mm:ss"
                                                ),

                                            MeetingKey =
                                                null,

                                            SegmentId =
                                                latest.SegmentId
                                        };

                                    _captionStore.Add(
                                        updatedCaption
                                    );
                                }
                            }
                            else
                            {
                                CaptionItem newCaption =
                                    new CaptionItem
                                    {
                                        Speaker =
                                            "Interviewer",

                                        Text =
                                            mergedText,

                                        Timestamp =
                                            DateTime.Now.ToString(
                                                "HH:mm:ss"
                                            ),

                                        MeetingKey =
                                            null,

                                        SegmentId =
                                            Guid.NewGuid()
                                                .ToString("N")
                                    };

                                _captionStore.Add(
                                    newCaption
                                );
                            }
                        }
                    }
                }

                _liveAudioBuffer.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Caption finalization failed: {ex}"
                );
            }
            finally
            {
                _finalizeScheduled =
                    false;
            }
        }


        private static string CleanWhisperText(
    string text
)
        {
            if (
                string.IsNullOrWhiteSpace(
                    text
                )
            )
            {
                return string.Empty;
            }


            text =
                text.Trim();


            // =========================================================
            // PURE NON-SPEECH CAPTIONS
            // =========================================================

            string[] ignoredExact =
            {
        "[BLANK_AUDIO]",

        "[music]",
        "(music)",
        "[Music]",
        "(upbeat music)",

        "[Applause]",
        "(applause)",

        "[Silence]",
        "(silence)",
        "(laughter)",
        "[Laughter]",
        "(laughing)",
        "[Laughing]",
        "(music playing)",
        "[MUSIC PLAYING]",

        "[Cough]",
        "[Coughing]",
        "(cough)",
        "(coughing)",
        "[cough]",
        "[coughing]",

        "[Keyboard]",
        "[Keyboard typing]",
        "[Typing]",
        "(keyboard)",
        "(keyboard typing)",
        "(typing)",

        "[Clicking]",
        "(clicking)",
        "[Mouse clicking]",
        "(mouse clicking)",

        "[Noise]",
        "(noise)",

        "[Breathing]",
        "(breathing)",

        "[Sniff]",
        "[Sniffing]",
        "(sniff)",
        "(sniffing)",

        "[Throat clearing]",
        "(throat clearing)",

        "[Sneeze]",
        "[Sneezing]",
        "(sneeze)",
        "(sneezing)"
    };


            foreach (
                string ignored
                in ignoredExact
            )
            {
                if (
                    string.Equals(
                        text,
                        ignored,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return string.Empty;
                }
            }


            // =========================================================
            // REMOVE NON-SPEECH MARKERS INSIDE OTHERWISE VALID TEXT
            // =========================================================

            string[] removableMarkers =
            {
        "[BLANK_AUDIO]",

        "[music]",
        "(music)",
        "(upbeat music)",

        "[applause]",
        "(applause)",

        "[cough]",
        "[coughing]",
        "(cough)",
        "(coughing)",

        "[keyboard]",
        "[keyboard typing]",
        "[typing]",
        "(keyboard)",
        "(keyboard typing)",
        "(typing)",

        "[mouse clicking]",
        "(mouse clicking)",
        "[clicking]",
        "(clicking)",

        "[noise]",
        "(noise)",

        "[breathing]",
        "(breathing)",

        "[sniff]",
        "[sniffing]",
        "(sniff)",
        "(sniffing)",

        "[throat clearing]",
        "(throat clearing)",

        "[sneeze]",
        "[sneezing]",
        "(sneeze)",
        "(sneezing)"
    };


            foreach (
                string marker
                in removableMarkers
            )
            {
                text =
                    text.Replace(
                        marker,
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase
                    );
            }


            // =========================================================
            // REMOVE COMMON LEADING CAPTION ARTIFACTS
            // =========================================================

            text =
                text.TrimStart(
                    ' ',
                    '-',
                    '•',
                    '–',
                    '—'
                );


            // =========================================================
            // NORMALIZE WHITESPACE
            // =========================================================

            text =
                string.Join(
                    " ",
                    text.Split(
                        new[]
                        {
                    ' ',
                    '\r',
                    '\n',
                    '\t'
                        },
                        StringSplitOptions.RemoveEmptyEntries
                    )
                );


            text =
                text.Trim();


            if (
                string.IsNullOrWhiteSpace(
                    text
                )
            )
            {
                return string.Empty;
            }


            return text;
        }

        private static double CalculateRms(
            byte[] pcm16
        )
        {
            if (
                pcm16 == null ||
                pcm16.Length < 2
            )
            {
                return 0;
            }

            double sumSquares = 0;

            int sampleCount =
                pcm16.Length / 2;

            for (
                int i = 0;
                i + 1 < pcm16.Length;
                i += 2
            )
            {
                short sample =
                    (short)(
                        pcm16[i] |
                        (pcm16[i + 1] << 8)
                    );

                double normalized =
                    sample / 32768.0;

                sumSquares +=
                    normalized * normalized;
            }

            return Math.Sqrt(
                sumSquares / sampleCount
            );
        }

        // =========================================================
        // DISPLAY AFFINITY
        // =========================================================

        private const uint WDA_EXCLUDEFROMCAPTURE =
            0x00000011;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private byte _windowAlpha = 255;
        private double _overlayOpacity = 1.0;
        private readonly EnumChildProc _clearWebViewLayeredCallback;


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


        private delegate bool EnumChildProc(
            IntPtr hWnd,
            IntPtr lParam
        );


        [DllImport(
            "user32.dll"
        )]
        private static extern bool EnumChildWindows(
            IntPtr hWndParent,
            EnumChildProc lpEnumFunc,
            IntPtr lParam
        );


        [DllImport(
            "user32.dll",
            EntryPoint = "GetWindowLong",
            SetLastError = true
        )]
        private static extern int GetWindowLong32(
            IntPtr hWnd,
            int nIndex
        );


        [DllImport(
            "user32.dll",
            EntryPoint = "GetWindowLongPtr",
            SetLastError = true
        )]
        private static extern IntPtr GetWindowLongPtr64(
            IntPtr hWnd,
            int nIndex
        );


        [DllImport(
            "user32.dll",
            EntryPoint = "SetWindowLong",
            SetLastError = true
        )]
        private static extern int SetWindowLong32(
            IntPtr hWnd,
            int nIndex,
            int dwNewLong
        );


        [DllImport(
            "user32.dll",
            EntryPoint = "SetWindowLongPtr",
            SetLastError = true
        )]
        private static extern IntPtr SetWindowLongPtr64(
            IntPtr hWnd,
            int nIndex,
            IntPtr dwNewLong
        );


        [DllImport(
            "user32.dll",
            SetLastError = true
        )]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags
        );


        private static IntPtr GetWindowLongPtr(
            IntPtr hWnd,
            int nIndex
        )
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }


        private static void SetWindowLongPtr(
            IntPtr hWnd,
            int nIndex,
            IntPtr value
        )
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(hWnd, nIndex, value);
                return;
            }

            SetWindowLong32(hWnd, nIndex, value.ToInt32());
        }


        // =========================================================
        // GLOBAL HOTKEYS
        // =========================================================

        private const int HOTKEY_LATEST_CAPTION = 1001;
        private const int HOTKEY_ALL_CAPTIONS = 1002;
        private const int HOTKEY_COPY_LATEST_AND_SEND = 1003;
        private const int HOTKEY_PASTE_AND_SEND = 1004;
        private const int HOTKEY_TOGGLE_BROWSER = 1005;
        private const int HOTKEY_SCREENSHOT = 1006;


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
        private const uint VK_X = 0x58;
        private const ushort VK_CONTROL = 0x11;

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const string ScreenshotAnalysisPrompt =
            "Please analyze the attached screenshot. If it contains a coding problem, written question, exam prompt, or other on-screen task, provide a correct and complete solution. If it shows code, identify any issues and include a corrected implementation. Present the answer clearly and precisely.";

        private bool _screenshotBusy;


        private const int WM_HOTKEY =
            0x0312;

        private const int WM_NCHITTEST =
            0x0084;

        private const int HTCLIENT =
            1;


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


        [DllImport(
            "user32.dll",
            SetLastError = true
        )]
        private static extern uint SendInput(
            uint nInputs,
            INPUT[] pInputs,
            int cbSize
        );


        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;

            public static int Size =>
                Marshal.SizeOf<INPUT>();
        }


        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;

            [FieldOffset(0)]
            public KEYBDINPUT ki;

            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }


        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public MainWindow()
        {
            _clearWebViewLayeredCallback =
                ClearWebViewChildLayeredStyle;

            InitializeComponent();

            _browserOverlay =
                new BrowserOverlayWindow();

            InitializeTrayIcon();

            _captionStore.Changed +=
                CaptionStore_Changed;


            _systemAudioCapture.AudioAvailable +=
                SystemAudioCapture_AudioAvailable;

            _microphoneCapture.AudioAvailable +=
                MicrophoneCapture_AudioAvailable;

            SourceInitialized +=
                MainWindow_SourceInitialized;

            Loaded +=
                MainWindow_Loaded;

            Closed +=
                MainWindow_Closed;

            LocationChanged +=
                (_, _) => QueueSyncBrowserOverlay();

            SizeChanged +=
                (_, _) => QueueSyncBrowserOverlay();

            LayoutUpdated +=
                (_, _) => QueueSyncBrowserOverlay();

            StateChanged +=
                MainWindow_StateChanged;
        }


        // =========================================================
        // WINDOW INITIALIZATION
        // =========================================================

        private void InitializeWhisper()
        {
            if (_whisperInitialized)
            {
                return;
            }

            string modelPath =
                System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "Models",
                    "ggml-base.en.bin"
                );

            _whisperService =
                new WhisperTranscriptionService(
                    modelPath
                );

            _whisperInitialized = true;

            System.IO.File.AppendAllText(
                System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "whisper.log"
                ),
                $"Whisper initialized: {DateTime.Now}{Environment.NewLine}"
            );
        }

        private void InitializeVoiceActivity()
        {
            string vadModelPath =
                System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "Models",
                    "ggml-silero-v6.2.0.bin"
                );

            _voiceActivityService =
                new VoiceActivityService(
                    vadModelPath
                );
        }

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
                Console.WriteLine(
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

            Forms.ToolStripMenuItem resetOpacityItem =
                new Forms.ToolStripMenuItem(
                    "Reset opacity to 100%"
                );

            resetOpacityItem.Click +=
                (_, _) =>
                {
                    Dispatcher.BeginInvoke(
                        new Action(ResetWindowOpacityToOpaque)
                    );
                };

            menu.Items.Add(
                showItem
            );

            menu.Items.Add(
                resetOpacityItem
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
                        if (_windowAlpha == 0)
                        {
                            ResetWindowOpacityToOpaque();
                        }

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
                        QueueSyncBrowserOverlay();
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
                        HidePrivateBrowserByHotkey();
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


            // If minimized, normalize first so WebView2 keeps
            // a valid composition surface.
            if (
                WindowState ==
                WindowState.Minimized
            )
            {
                WindowState =
                    WindowState.Normal;
            }


            _savedLeft =
                Left;

            _savedTop =
                Top;

            _savedPositionValid =
                true;


            // IMPORTANT:
            // Do not call Hide().
            // Keep the same WPF + WebView2 visual tree alive
            // and move the window outside the visible desktop.
            Topmost =
                false;


            Left =
                SystemParameters.VirtualScreenLeft -
                Math.Max(
                    ActualWidth > 0
                        ? ActualWidth
                        : Width,
                    800
                ) -
                300;


            Top =
                SystemParameters.VirtualScreenTop -
                Math.Max(
                    ActualHeight > 0
                        ? ActualHeight
                        : Height,
                    600
                ) -
                300;


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


            _browserHiddenByHotkey =
                false;


            Topmost =
                true;

            Activate();

            Focus();


            if (
                Browser.CoreWebView2 !=
                null
            )
            {
                _browserOverlay.Activate();
                Browser.Focus();
            }

            QueueSyncBrowserOverlay();
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
                Dispatcher.BeginInvoke(
                    new Action(
                        () =>
                        {
                            HidePrivateBrowserByHotkey();
                        }
                    )
                );
            }
        }

        protected override void OnClosing(
    System.ComponentModel.CancelEventArgs e
)
        {
            if (!_isExiting)
            {
                e.Cancel =
                    true;

                Dispatcher.BeginInvoke(
                    new Action(
                        () =>
                        {
                            HidePrivateBrowserByHotkey();
                        }
                    )
                );

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
                ShowBrowserOverlay();

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


                CoreWebView2EnvironmentOptions environmentOptions =
                    new CoreWebView2EnvironmentOptions
                    {
                        AreBrowserExtensionsEnabled =
                            true
                    };


                CoreWebView2Environment environment =
                    await CoreWebView2Environment
                        .CreateAsync(
                            null,
                            profilePath,
                            environmentOptions
                        );


                await Browser
                    .EnsureCoreWebView2Async(
                        environment
                    );

                // Transparent (0,0,0,0) paints black in WebView2. Keep an
                // unlit white so ChatGPT's own rgba backgrounds can fade.
                Browser.DefaultBackgroundColor =
                    Drawing.Color.FromArgb(0, 255, 255, 255);

                Browser.CoreWebView2.Profile
                    .PreferredTrackingPreventionLevel =
                    CoreWebView2TrackingPreventionLevel.None;

                await Browser.CoreWebView2
                    .AddScriptToExecuteOnDocumentCreatedAsync(
                        BuildChatGptOpacityScript("1")
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

                EnableCaptureProtection(
                    GetBrowserOverlayHandle()
                );

                System.Diagnostics.Debug.WriteLine(
                    $"Capture protection after WebView2 init: {protectedOk}"
                );

                ApplyWindowOpacity(100);
                if (OpacitySlider != null)
                {
                    OpacitySlider.Value = 100;
                }
                EnsureCaptureProtectionStillEnabled();


                Browser.CoreWebView2
                    .NavigationCompleted +=
                    CoreWebView2_NavigationCompleted;


                Navigate(
                    "https://chatgpt.com"
                );

                try
                {
                    _systemAudioCapture.Start();

                    System.Diagnostics.Debug.WriteLine(
                        "System audio capture started."
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"System audio capture failed: {ex}"
                    );

                    WpfMessageBox.Show(
                        $"System audio capture failed:\n\n{ex.Message}",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Warning
                    );
                }

                try
                {
                    InitializeWhisper();

                    System.Diagnostics.Debug.WriteLine(
                        "Whisper initialized successfully."
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Whisper initialization failed: {ex}"
                    );

                    WpfMessageBox.Show(
                        $"Whisper initialization failed:\n\n{ex.Message}",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Error
                    );
                }

                try
                {
                    InitializeVoiceActivity();

                    System.Diagnostics.Debug.WriteLine(
                        "Voice activity detection initialized successfully."
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"VAD initialization failed: {ex}"
                    );

                    WpfMessageBox.Show(
                        $"Voice activity detection initialization failed:\n\n{ex.Message}",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Warning
                    );
                }

                try
                {
                    _microphoneCapture.Start();

                    System.Diagnostics.Debug.WriteLine(
                        "Microphone capture started."
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Microphone capture failed: {ex}"
                    );

                    WpfMessageBox.Show(
                        $"Microphone capture failed:\n\n{ex.Message}",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Warning
                    );
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"PrivateBrowser initialization failed:\n\n{ex.Message}",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Error
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
                &&
                !address.StartsWith(
                    "chrome-extension://",
                    StringComparison.OrdinalIgnoreCase
                )
                &&
                !address.StartsWith(
                    "file://",
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

            _ = ApplyChatGptPageOpacityAsync();
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


        private void ExtensionsButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            if (
                Browser.CoreWebView2 == null
            )
            {
                WpfMessageBox.Show(
                    "The browser is still starting. Try again in a moment.",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information
                );

                return;
            }

            ExtensionsWindow window =
                new ExtensionsWindow(
                    Browser.CoreWebView2,
                    url =>
                    {
                        Browser.CoreWebView2?.Navigate(
                            url
                        );
                    }
                )
                {
                    Owner =
                        this
                };

            window.ShowDialog();
        }


        private void AddressBar_KeyDown(
            object sender,
            WpfKeyEventArgs e
        )
        {
            if (
                e.Key ==
                WpfKey.Enter
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
            return EnableCaptureProtection(_windowHandle);
        }


        private bool EnableCaptureProtection(
            IntPtr hwnd
        )
        {
            if (
                hwnd ==
                IntPtr.Zero
            )
            {
                return false;
            }


            bool setOk =
                SetWindowDisplayAffinity(
                    hwnd,
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
                    hwnd,
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


        private void EnsureCaptureProtectionStillEnabled()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            bool getOk =
                GetWindowDisplayAffinity(
                    _windowHandle,
                    out uint currentAffinity
                );

            if (
                getOk &&
                currentAffinity ==
                WDA_EXCLUDEFROMCAPTURE
            )
            {
                return;
            }

            EnableCaptureProtection();
            EnableCaptureProtection(
                GetBrowserOverlayHandle()
            );
        }


        private void ShowBrowserOverlay()
        {
            if (_browserOverlay == null)
            {
                return;
            }

            AssignBrowserOverlayOwner();
            _browserOverlay.ShowActivated = false;

            if (IsVisible && !_browserOverlay.IsVisible)
            {
                _browserOverlay.Show();
            }

            QueueSyncBrowserOverlay();
        }


        private void AssignBrowserOverlayOwner()
        {
            if (
                _browserOverlay == null ||
                !IsVisible ||
                _browserOverlay.Owner == this
            )
            {
                return;
            }

            _browserOverlay.Owner = this;
        }


        private void QueueSyncBrowserOverlay()
        {
            if (_overlaySyncQueued)
            {
                return;
            }

            _overlaySyncQueued = true;

            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        _overlaySyncQueued = false;
                        SyncBrowserOverlay();
                    }
                ),
                DispatcherPriority.Render
            );
        }


        private void SyncBrowserOverlay()
        {
            if (
                _browserOverlay == null ||
                BrowserHost == null
            )
            {
                return;
            }

            if (
                !IsVisible ||
                _browserHiddenByHotkey ||
                BrowserHost.ActualWidth < 2 ||
                BrowserHost.ActualHeight < 2
            )
            {
                if (_browserOverlay.IsVisible)
                {
                    _browserOverlay.Hide();
                }

                return;
            }

            if (!_browserOverlay.IsVisible)
            {
                AssignBrowserOverlayOwner();
                _browserOverlay.Show();
            }

            try
            {
                System.Windows.Point pixels =
                    BrowserHost.PointToScreen(
                        new System.Windows.Point(0, 0)
                    );

                PresentationSource? source =
                    PresentationSource.FromVisual(this);

                if (source?.CompositionTarget != null)
                {
                    System.Windows.Point dips =
                        source.CompositionTarget.TransformFromDevice
                            .Transform(pixels);

                    _browserOverlay.Left = dips.X;
                    _browserOverlay.Top = dips.Y;
                }
                else
                {
                    _browserOverlay.Left = pixels.X;
                    _browserOverlay.Top = pixels.Y;
                }

                _browserOverlay.Width = BrowserHost.ActualWidth;
                _browserOverlay.Height = BrowserHost.ActualHeight;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Browser overlay sync failed: {ex.Message}"
                );
            }

            RestoreWebViewHitTesting();
            EnableCaptureProtection(GetBrowserOverlayHandle());
        }


        private IntPtr GetBrowserOverlayHandle()
        {
            if (_browserOverlay == null)
            {
                return IntPtr.Zero;
            }

            return new WindowInteropHelper(_browserOverlay).EnsureHandle();
        }


        private void ApplyWindowOpacity(
            double percent
        )
        {
            percent =
                Math.Clamp(
                    percent,
                    10,
                    100
                );

            // Do not set Window.Opacity. That blends toward black on this
            // WebView2 stack instead of showing windows behind.
            Opacity = 1.0;

            _overlayOpacity =
                percent / 100.0;

            _windowAlpha =
                (byte)Math.Clamp(
                    Math.Round(_overlayOpacity * 255.0),
                    26,
                    255
                );

            if (NavBar != null)
            {
                NavBar.Opacity = _overlayOpacity;
            }

            if (CaptionPanel != null)
            {
                CaptionPanel.Opacity = _overlayOpacity;
            }

            if (CaptionSplitter != null)
            {
                CaptionSplitter.Opacity = _overlayOpacity;
            }

            if (CaptionToggleButton != null)
            {
                CaptionToggleButton.Opacity = _overlayOpacity;
            }

            RestoreWebViewHitTesting();
            _ = ApplyChatGptPageOpacityAsync();
            QueueSyncBrowserOverlay();
            EnsureCaptureProtectionStillEnabled();
        }


        private async Task ApplyChatGptPageOpacityAsync()
        {
            if (Browser?.CoreWebView2 == null)
            {
                return;
            }

            string opacityLiteral =
                _overlayOpacity.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture
                );

            try
            {
                await Browser.CoreWebView2.ExecuteScriptAsync(
                    BuildChatGptOpacityScript(opacityLiteral)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Page opacity script failed: {ex.Message}"
                );
            }
        }


        private static string BuildChatGptOpacityScript(
            string opacityLiteral
        )
        {
            return $$"""
                (() => {
                    window.__pbOverlayOpacity = {{opacityLiteral}};

                    const STYLE_ID = 'pb-overlay-opacity';

                    function parseRgba(input) {
                        if (!input || input === 'transparent') {
                            return null;
                        }
                        const raw = String(input).trim();
                        const rgb = raw.match(
                            /rgba?\(\s*([0-9.]+)\s*,\s*([0-9.]+)\s*,\s*([0-9.]+)(?:\s*,\s*([0-9.]+))?\s*\)/i
                        );
                        if (rgb) {
                            return {
                                r: +rgb[1],
                                g: +rgb[2],
                                b: +rgb[3],
                                a: rgb[4] === undefined ? 1 : +rgb[4]
                            };
                        }
                        const hex6 = raw.match(/^#([0-9a-f]{6})$/i);
                        if (hex6) {
                            return {
                                r: parseInt(hex6[1].slice(0, 2), 16),
                                g: parseInt(hex6[1].slice(2, 4), 16),
                                b: parseInt(hex6[1].slice(4, 6), 16),
                                a: 1
                            };
                        }
                        const hex3 = raw.match(/^#([0-9a-f]{3})$/i);
                        if (hex3) {
                            return {
                                r: parseInt(hex3[1][0] + hex3[1][0], 16),
                                g: parseInt(hex3[1][1] + hex3[1][1], 16),
                                b: parseInt(hex3[1][2] + hex3[1][2], 16),
                                a: 1
                            };
                        }
                        return null;
                    }

                    function isDarkTheme() {
                        const root = document.documentElement;
                        const body = document.body;
                        const cls = (
                            (root && root.className || '') + ' ' +
                            (body && body.className || '') + ' ' +
                            (root && root.getAttribute('data-theme') || '')
                        ).toLowerCase();
                        if (/\bdark\b/.test(cls)) {
                            return true;
                        }
                        if (/\blight\b/.test(cls)) {
                            return false;
                        }
                        if (window.__pbDefaultMain) {
                            return window.__pbDefaultMain.r < 80;
                        }
                        return !!(
                            window.matchMedia &&
                            window.matchMedia('(prefers-color-scheme: dark)').matches
                        );
                    }

                    function isSurface(c) {
                        if (!c || c.a < 0.08) {
                            return false;
                        }
                        const max = Math.max(c.r, c.g, c.b);
                        const min = Math.min(c.r, c.g, c.b);
                        return (max - min) <= 45 && (max >= 210 || max <= 80);
                    }

                    function fallbackMain() {
                        return isDarkTheme()
                            ? { r: 33, g: 33, b: 33 }
                            : { r: 255, g: 255, b: 255 };
                    }

                    function fallbackSidebar() {
                        return isDarkTheme()
                            ? { r: 32, g: 33, b: 35 }
                            : { r: 247, g: 247, b: 248 };
                    }

                    function firstVar(cs, names) {
                        for (let i = 0; i < names.length; i++) {
                            const value = cs.getPropertyValue(names[i]).trim();
                            if (value) {
                                return value;
                            }
                        }
                        return '';
                    }

                    function tryCaptureDefaults() {
                        if (window.__pbCapturedDefaults) {
                            return;
                        }
                        const cs = getComputedStyle(document.documentElement);
                        const main = parseRgba(
                            firstVar(cs, [
                                '--token-main-surface-primary',
                                '--main-surface-primary',
                                '--bg-primary',
                                '--background'
                            ])
                        );
                        if (!main || main.a < 0.95) {
                            return;
                        }
                        const sidebar = parseRgba(
                            firstVar(cs, [
                                '--token-sidebar-surface-primary',
                                '--sidebar-surface-primary'
                            ])
                        );
                        window.__pbDefaultMain = {
                            r: main.r,
                            g: main.g,
                            b: main.b
                        };
                        window.__pbDefaultSidebar =
                            sidebar && sidebar.a >= 0.95
                                ? { r: sidebar.r, g: sidebar.g, b: sidebar.b }
                                : fallbackSidebar();
                        window.__pbCapturedDefaults = true;
                    }

                    function rgbaOf(rgb, opacity) {
                        const color = rgb || fallbackMain();
                        return 'rgba(' +
                            Math.round(color.r) + ', ' +
                            Math.round(color.g) + ', ' +
                            Math.round(color.b) + ', ' +
                            opacity + ')';
                    }

                    function ensureStyle() {
                        tryCaptureDefaults();
                        const opacity = window.__pbOverlayOpacity;
                        const main = rgbaOf(
                            window.__pbDefaultMain || fallbackMain(),
                            opacity
                        );
                        const sidebar = rgbaOf(
                            window.__pbDefaultSidebar || fallbackSidebar(),
                            opacity
                        );
                        let style = document.getElementById(STYLE_ID);
                        if (!style) {
                            style = document.createElement('style');
                            style.id = STYLE_ID;
                            (document.head || document.documentElement)
                                .appendChild(style);
                        }
                        style.textContent =
                            'html, body, #__next, #root {' +
                            '  background: ' + main + ' !important;' +
                            '  background-color: ' + main + ' !important;' +
                            '  opacity: 1 !important;' +
                            '}' +
                            'html {' +
                            '  --background: ' + main + ' !important;' +
                            '  --bg-primary: ' + main + ' !important;' +
                            '  --bg-secondary: ' + main + ' !important;' +
                            '  --bg-tertiary: ' + main + ' !important;' +
                            '  --main-surface-primary: ' + main + ' !important;' +
                            '  --main-surface-secondary: ' + main + ' !important;' +
                            '  --main-surface-tertiary: ' + main + ' !important;' +
                            '  --token-bg-primary: ' + main + ' !important;' +
                            '  --token-main-surface-primary: ' + main + ' !important;' +
                            '  --token-main-surface-secondary: ' + main + ' !important;' +
                            '  --token-main-surface-tertiary: ' + main + ' !important;' +
                            '  --sidebar-surface-primary: ' + sidebar + ' !important;' +
                            '  --sidebar-surface-secondary: ' + sidebar + ' !important;' +
                            '  --token-sidebar-surface-primary: ' + sidebar + ' !important;' +
                            '  --token-sidebar-surface-secondary: ' + sidebar + ' !important;' +
                            '}' +
                            '[data-pb-bg="main"] {' +
                            '  background: ' + main + ' !important;' +
                            '  background-color: ' + main + ' !important;' +
                            '}' +
                            '[data-pb-bg="sidebar"] {' +
                            '  background: ' + sidebar + ' !important;' +
                            '  background-color: ' + sidebar + ' !important;' +
                            '}';
                    }

                    function mark(el, kind) {
                        if (!el || el.nodeType !== 1) {
                            return;
                        }
                        if (el.getAttribute('data-pb-bg') !== kind) {
                            el.setAttribute('data-pb-bg', kind);
                        }
                    }

                    function isComposerRoot(el) {
                        if (!el) {
                            return false;
                        }
                        const testId = el.getAttribute('data-testid') || '';
                        if (el.tagName !== 'FORM' && testId !== 'composer') {
                            return false;
                        }
                        const r = el.getBoundingClientRect();
                        const vw = window.innerWidth || 1;
                        const vh = window.innerHeight || 1;
                        return r.height > 24 &&
                            r.height < vh * 0.42 &&
                            r.width > vw * 0.2;
                    }

                    function walk(el, inSidebar) {
                        if (!el || el.nodeType !== 1) {
                            return;
                        }
                        const tag = el.tagName;
                        if (
                            tag === 'SCRIPT' ||
                            tag === 'STYLE' ||
                            tag === 'SVG' ||
                            tag === 'IMG' ||
                            tag === 'CANVAS' ||
                            tag === 'VIDEO'
                        ) {
                            return;
                        }

                        const r = el.getBoundingClientRect();
                        const vw = window.innerWidth || 1;
                        const vh = window.innerHeight || 1;

                        const leftBar = !inSidebar &&
                            r.width >= 36 &&
                            r.width <= 440 &&
                            r.height >= vh * 0.45 &&
                            r.left < 90 &&
                            r.top < vh * 0.25;

                        const composer = !inSidebar &&
                            !leftBar &&
                            isComposerRoot(el);

                        const nextSidebar = inSidebar || leftBar || composer;
                        const bg = parseRgba(
                            getComputedStyle(el).backgroundColor
                        );

                        if (leftBar || inSidebar || composer) {
                            if (isSurface(bg)) {
                                mark(el, 'sidebar');
                            }
                        } else {
                            const bigCanvas =
                                r.width >= vw * 0.28 &&
                                r.height >= vh * 0.28;
                            if (bigCanvas && isSurface(bg)) {
                                mark(el, 'main');
                            }
                        }

                        const kids = el.children;
                        for (let i = 0; i < kids.length; i++) {
                            walk(kids[i], nextSidebar);
                        }
                    }

                    function apply() {
                        ensureStyle();
                        if (!document.body) {
                            return;
                        }
                        document.querySelectorAll('[data-pb-bg]').forEach(
                            function (node) {
                                node.removeAttribute('data-pb-bg');
                            }
                        );
                        walk(document.body, false);
                    }

                    window.__pbApplyOverlayOpacity = apply;
                    apply();

                    if (!window.__pbOpacityObserver && document.documentElement) {
                        let timer = 0;
                        window.__pbOpacityObserver = new MutationObserver(
                            function (mutations) {
                                for (let i = 0; i < mutations.length; i++) {
                                    const t = mutations[i].target;
                                    if (
                                        t &&
                                        t.closest &&
                                        t.closest('[data-message-author-role]')
                                    ) {
                                        continue;
                                    }
                                    if (timer) {
                                        clearTimeout(timer);
                                    }
                                    timer = setTimeout(apply, 180);
                                    return;
                                }
                            }
                        );
                        window.__pbOpacityObserver.observe(
                            document.documentElement,
                            { childList: true, subtree: true }
                        );
                    }
                })()
                """;
        }


        private void RestoreWebViewHitTesting()
        {
            IntPtr browserHwnd =
                IntPtr.Zero;

            try
            {
                if (Browser != null)
                {
                    browserHwnd =
                        Browser.Handle;
                }
            }
            catch
            {
                return;
            }

            if (browserHwnd == IntPtr.Zero)
            {
                return;
            }

            ClearWindowLayeredStyle(browserHwnd);
            EnumChildWindows(
                browserHwnd,
                _clearWebViewLayeredCallback,
                IntPtr.Zero
            );
        }


        private bool ClearWebViewChildLayeredStyle(
            IntPtr hWnd,
            IntPtr lParam
        )
        {
            ClearWindowLayeredStyle(hWnd);
            return true;
        }


        private void ClearWindowLayeredStyle(
            IntPtr hwnd
        )
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int exStyle = GetExtendedStyle(hwnd);
            if ((exStyle & WS_EX_LAYERED) == 0)
            {
                return;
            }

            SetExtendedStyle(
                hwnd,
                exStyle & ~WS_EX_LAYERED
            );
        }


        private void NavBar_MouseLeftButtonDown(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e
        )
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            DependencyObject? current =
                e.OriginalSource as DependencyObject;

            while (current != null)
            {
                if (
                    current is WpfButton ||
                    current is Slider ||
                    current is System.Windows.Controls.TextBox
                )
                {
                    return;
                }

                current =
                    VisualTreeHelper.GetParent(current);
            }

            try
            {
                DragMove();
            }
            catch
            {
            }
        }


        private void CloseOverlayButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            Close();
        }


        private void ResetWindowOpacityToOpaque()
        {
            if (OpacitySlider != null)
            {
                OpacitySlider.Value = 100;
                return;
            }

            ApplyWindowOpacity(100);
        }


        private static int GetExtendedStyle(
            IntPtr hwnd
        )
        {
            return unchecked(
                (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64()
            );
        }


        private void SetExtendedStyle(
            IntPtr hwnd,
            int exStyle
        )
        {
            SetWindowLongPtr(
                hwnd,
                GWL_EXSTYLE,
                new IntPtr(exStyle)
            );

            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SWP_NOMOVE |
                SWP_NOSIZE |
                SWP_NOZORDER |
                SWP_NOACTIVATE |
                SWP_FRAMECHANGED
            );
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
            UnregisterHotKey(_windowHandle, HOTKEY_SCREENSHOT);

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

            RegisterRequiredHotkey(
                HOTKEY_SCREENSHOT,
                VK_X,
                "Ctrl + Shift + X"
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

            WpfMessageBox.Show(
                $"{displayName} could not be registered.\n\n" +
                "Another application may already be using this global shortcut.\n" +
                $"Windows error: {error}",
                "PrivateBrowser Hotkey Error",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Warning
            );
        }


        private bool IsScreenPointOverBrowser(
            IntPtr lParam
        )
        {
            if (
                Browser == null ||
                !Browser.IsVisible ||
                Browser.ActualWidth <= 0 ||
                Browser.ActualHeight <= 0
            )
            {
                return false;
            }

            try
            {
                int packed =
                    unchecked((int)lParam.ToInt64());

                int screenX =
                    (short)(packed & 0xFFFF);

                int screenY =
                    (short)((packed >> 16) & 0xFFFF);

                System.Windows.Point local =
                    Browser.PointFromScreen(
                        new System.Windows.Point(
                            screenX,
                            screenY
                        )
                    );

                return local.X >= 0 &&
                    local.Y >= 0 &&
                    local.X < Browser.ActualWidth &&
                    local.Y < Browser.ActualHeight;
            }
            catch
            {
                return false;
            }
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


                case HOTKEY_SCREENSHOT:

                    handled = true;

                    Dispatcher.BeginInvoke(
                        new Action(
                            async () =>
                            {
                                await CaptureScreenshotAndAttachToChatGptAsync();
                            }
                        )
                    );

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
                    FormatCaptionLine(
                        latestItem
                    );


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
                string recentTranscript =
                    _captionStore.GetRecentTranscript(
                        RecentTranscriptBlockCount
                    );

                if (string.IsNullOrWhiteSpace(recentTranscript))
                {
                    WpfMessageBox.Show(
                        "There is no recent transcript to send.",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Information
                    );

                    return;
                }

                string text =
                    BuildInterviewAssistPrompt(
                        recentTranscript
                    );

                bool copied =
                    await TrySetClipboardTextAsync(
                        text
                    );

                if (!copied)
                {
                    WpfMessageBox.Show(
                        "Could not copy the recent transcript.",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Warning
                    );

                    return;
                }

                await PasteIntoChatGptAndSendAsync(
                    text
                );
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"Ctrl + Shift + S failed:\n\n{ex.Message}",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Error
                );
            }
        }


        private async void TranscriptSendButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            e.Handled = true;

            if (
                sender is not WpfButton button ||
                button.Tag is not CaptionItem item
            )
            {
                return;
            }

            string text =
                FormatCaptionLine(
                    item
                );

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                bool copied =
                    await TrySetClipboardTextAsync(
                        text
                    );

                if (!copied)
                {
                    WpfMessageBox.Show(
                        "Could not copy this caption.",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Warning
                    );

                    return;
                }

                await PasteIntoChatGptAndSendAsync(
                    text
                );
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"Send to ChatGPT failed:\n\n{ex.Message}",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Error
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

            try
            {
                _browserOverlay?.Close();
            }
            catch
            {
            }

            _systemAudioCapture.AudioAvailable -=
                SystemAudioCapture_AudioAvailable;
            _microphoneCapture.AudioAvailable -=
                MicrophoneCapture_AudioAvailable;

            _systemAudioCapture.Dispose();
            _microphoneCapture.Dispose();

            _whisperService?.Dispose();
            _whisperService =
                null;

            _voiceActivityService?.Dispose();
            _voiceActivityService =
                null;

            _liveAudioBuffer.Clear();
            _microphoneLiveAudioBuffer.Clear();

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
                UnregisterHotKey(
                    _windowHandle,
                    HOTKEY_SCREENSHOT
                );
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
            _liveAudioBuffer.Clear();
            _microphoneLiveAudioBuffer.Clear();

            lock (_interviewerWhisperQueueLock)
            {
                _pendingInterviewerAudio =
                    null;
            }

            lock (_microphoneWhisperQueueLock)
            {
                _pendingMicrophoneAudio =
                    null;
            }

            _captionStore.Clear();

            UpdateCaptionPanel();
        }


        private async void ScreenshotButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await CaptureScreenshotAndAttachToChatGptAsync();
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
                        WpfMessageBox.Show(
                            "Clipboard does not contain text.",
                            "PrivateBrowser",
                            WpfMessageBoxButton.OK,
                            WpfMessageBoxImage.Information
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

                WpfMessageBox.Show(
                    $"Paste & Send failed:\n\n{ex.Message}",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Warning
                );
            }
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
                if (Browser.CoreWebView2 == null)
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
                    WpfMessageBox.Show(
                        "Please open ChatGPT first.",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Information
                    );

                    return;
                }

                Drawing.Rectangle bounds =
                    GetScreenshotBounds();

                if (
                    bounds.Width <= 0 ||
                    bounds.Height <= 0
                )
                {
                    WpfMessageBox.Show(
                        "Could not determine a screen to capture.",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Warning
                    );

                    return;
                }

                bool hidForCapture = false;

                if (!_browserHiddenByHotkey)
                {
                    HidePrivateBrowserByHotkey();
                    hidForCapture = true;
                    await Task.Delay(180);
                }

                Drawing.Bitmap bitmap;

                try
                {
                    bitmap = CaptureScreenBounds(bounds);
                }
                finally
                {
                    if (hidForCapture)
                    {
                        RestorePrivateBrowserFromHotkey();
                    }
                }

                using (bitmap)
                {
                    tempPath = SaveScreenshotPng(bitmap);
                    await SetClipboardImageAsync(bitmap);
                }

                Activate();
                Browser.Focus();
                await Task.Delay(120);

                bool attached =
                    await AttachScreenshotFileToChatGptAsync(tempPath);

                if (!attached)
                {
                    await FocusChatGptComposerAsync();
                    await Task.Delay(80);
                    await PasteClipboardImageIntoChatGptAsync();
                    await Task.Delay(450);
                }
                else
                {
                    await Task.Delay(350);
                }

                await PasteIntoChatGptAndSendAsync(
                    ScreenshotAnalysisPrompt,
                    send: false,
                    replaceExisting: false
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Screenshot capture failed: {ex}"
                );

                WpfMessageBox.Show(
                    $"Screenshot capture failed:\n\n{ex.Message}",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Warning
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

        private Drawing.Rectangle GetScreenshotBounds()
        {
            Forms.Screen? primary =
                Forms.Screen.PrimaryScreen;

            if (
                primary != null &&
                primary.Bounds.Width > 0 &&
                primary.Bounds.Height > 0
            )
            {
                return primary.Bounds;
            }

            return Forms.SystemInformation.VirtualScreen;
        }

        private static Drawing.Bitmap CaptureScreenBounds(
            Drawing.Rectangle bounds
        )
        {
            var bitmap = new Drawing.Bitmap(
                bounds.Width,
                bounds.Height,
                Drawing.Imaging.PixelFormat.Format32bppArgb
            );

            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    bounds.Size,
                    Drawing.CopyPixelOperation.SourceCopy
                );
            }

            return bitmap;
        }

        private static string SaveScreenshotPng(
            Drawing.Bitmap bitmap
        )
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "PrivateBrowser"
            );

            Directory.CreateDirectory(folder);

            string path = Path.Combine(
                folder,
                $"screenshot-{Guid.NewGuid():N}.png"
            );

            bitmap.Save(path, Drawing.Imaging.ImageFormat.Png);
            return path;
        }

        private async Task SetClipboardImageAsync(
            Drawing.Bitmap bitmap
        )
        {
            using var pngStream = new MemoryStream();
            bitmap.Save(pngStream, Drawing.Imaging.ImageFormat.Png);
            byte[] pngBytes = pngStream.ToArray();

            var image = new BitmapImage();
            using (var loadStream = new MemoryStream(pngBytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = loadStream;
                image.EndInit();
            }

            image.Freeze();

            const int maxAttempts = 12;
            const int delayMs = 80;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var data = new WpfDataObject();
                    data.SetImage(image);
                    data.SetData("PNG", new MemoryStream(pngBytes));
                    WpfClipboard.SetDataObject(data, true);
                    return;
                }
                catch (COMException ex)
                when (
                    unchecked((uint)ex.HResult) == 0x800401D0
                )
                {
                    await Task.Delay(delayMs);
                }
            }
        }

        private async Task<bool> AttachScreenshotFileToChatGptAsync(
            string pngPath
        )
        {
            if (
                Browser.CoreWebView2 == null ||
                string.IsNullOrWhiteSpace(pngPath) ||
                !File.Exists(pngPath)
            )
            {
                return false;
            }

            try
            {
                const string markScript =
                    """
                    (() => {
                        document.querySelectorAll('[data-pb-upload]').forEach((el) => {
                            el.removeAttribute('data-pb-upload');
                        });

                        const inputs = Array.from(document.querySelectorAll('input[type="file"]'));
                        const target = inputs.find((el) => {
                            const accept = (el.accept || '').toLowerCase();
                            return accept.includes('image') ||
                                accept.includes('png') ||
                                accept.includes('*') ||
                                accept === '';
                        }) || inputs[0];

                        if (!target) {
                            const attach = document.querySelector(
                                'button[aria-label*="Attach" i], button[aria-label*="Add files" i], button[aria-label*="Upload" i], button[data-testid="composer-plus-btn"]'
                            );
                            if (attach) {
                                attach.click();
                            }
                            return false;
                        }

                        target.setAttribute('data-pb-upload', '1');
                        return true;
                    })()
                    """;

                string marked =
                    await Browser.CoreWebView2.ExecuteScriptAsync(markScript);

                if (
                    marked == null ||
                    !marked.Contains("true", StringComparison.OrdinalIgnoreCase)
                )
                {
                    await Task.Delay(220);
                    marked =
                        await Browser.CoreWebView2.ExecuteScriptAsync(markScript);
                }

                if (
                    marked == null ||
                    !marked.Contains("true", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return false;
                }

                await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "DOM.enable",
                    "{}"
                );

                string documentJson =
                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "DOM.getDocument",
                        "{\"depth\":0,\"pierce\":true}"
                    );

                using JsonDocument document = JsonDocument.Parse(documentJson);
                int rootNodeId = document.RootElement
                    .GetProperty("root")
                    .GetProperty("nodeId")
                    .GetInt32();

                string queryJson = JsonSerializer.Serialize(
                    new
                    {
                        nodeId = rootNodeId,
                        selector = "input[type=\"file\"][data-pb-upload=\"1\"]"
                    }
                );

                string nodeJson =
                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "DOM.querySelector",
                        queryJson
                    );

                using JsonDocument nodeDocument = JsonDocument.Parse(nodeJson);
                int nodeId = nodeDocument.RootElement
                    .GetProperty("nodeId")
                    .GetInt32();

                if (nodeId == 0)
                {
                    return false;
                }

                string filesJson = JsonSerializer.Serialize(
                    new
                    {
                        files = new[] { pngPath },
                        nodeId
                    }
                );

                await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "DOM.setFileInputFiles",
                    filesJson
                );

                return true;
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
            if (Browser.CoreWebView2 == null)
            {
                return;
            }

            const string script =
                """
                (() => {
                    const prompt = document.querySelector('#prompt-textarea, [data-testid="prompt-textarea"], div.ProseMirror[contenteditable="true"]');
                    if (!prompt) {
                        return false;
                    }
                    prompt.focus();
                    prompt.click();
                    return true;
                })()
                """;

            await Browser.CoreWebView2.ExecuteScriptAsync(script);
        }

        private async Task PasteClipboardImageIntoChatGptAsync()
        {
            if (Browser.CoreWebView2 != null)
            {
                try
                {
                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Input.dispatchKeyEvent",
                        "{\"type\":\"keyDown\",\"modifiers\":2,\"windowsVirtualKeyCode\":17,\"key\":\"Control\",\"code\":\"ControlLeft\"}"
                    );
                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Input.dispatchKeyEvent",
                        "{\"type\":\"keyDown\",\"modifiers\":2,\"windowsVirtualKeyCode\":86,\"key\":\"v\",\"code\":\"KeyV\"}"
                    );
                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Input.dispatchKeyEvent",
                        "{\"type\":\"keyUp\",\"modifiers\":2,\"windowsVirtualKeyCode\":86,\"key\":\"v\",\"code\":\"KeyV\"}"
                    );
                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Input.dispatchKeyEvent",
                        "{\"type\":\"keyUp\",\"modifiers\":0,\"windowsVirtualKeyCode\":17,\"key\":\"Control\",\"code\":\"ControlLeft\"}"
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"CDP paste failed: {ex}"
                    );
                }
            }

            SendCtrlV();
        }

        private static void SendCtrlV()
        {
            INPUT[] inputs = new INPUT[4];

            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = VK_CONTROL;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = (ushort)VK_V;

            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].U.ki.wVk = (ushort)VK_V;
            inputs[2].U.ki.dwFlags = KEYEVENTF_KEYUP;

            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].U.ki.wVk = VK_CONTROL;
            inputs[3].U.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput((uint)inputs.Length, inputs, INPUT.Size);
        }

        private async Task PasteIntoChatGptAndSendAsync(
            string text,
            bool send = true,
            bool replaceExisting = true
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
                WpfMessageBox.Show(
                    "Please open ChatGPT first.",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information
                );

                return;
            }

            Activate();
            Browser.Focus();
            await Task.Delay(80);

            string encodedText =
                System.Text.Json.JsonSerializer.Serialize(
                    text
                );

            string shouldSendJs =
                send ? "true" : "false";

            string replaceExistingJs =
                replaceExisting ? "true" : "false";

            string script =
                $$"""
                (async () => {
                    const text = {{encodedText}};
                    const shouldSend = {{shouldSendJs}};
                    const replaceExisting = {{replaceExistingJs}};

                    const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

                    const isVisible = (el) => {
                        if (!el) {
                            return false;
                        }

                        const style = window.getComputedStyle(el);
                        if (
                            style.display === 'none' ||
                            style.visibility === 'hidden' ||
                            style.opacity === '0'
                        ) {
                            return false;
                        }

                        const rect = el.getBoundingClientRect();
                        return rect.width > 0 && rect.height > 0;
                    };

                    const normalize = (value) =>
                        (value || '').replace(/\s+/g, ' ').trim();

                    const composerHasText = (el, expected) => {
                        const current = normalize(el.innerText || el.value || '');
                        const target = normalize(expected);
                        if (!current || !target) {
                            return false;
                        }

                        const sample = target.slice(0, Math.min(48, target.length));
                        return current.includes(sample);
                    };

                    const findComposer = () => {
                        const prompt = document.querySelector('#prompt-textarea');
                        if (prompt) {
                            if (prompt.isContentEditable || prompt instanceof HTMLTextAreaElement) {
                                if (isVisible(prompt)) {
                                    return prompt;
                                }
                            }

                            const nested = prompt.querySelector('[contenteditable="true"], textarea');
                            if (isVisible(nested)) {
                                return nested;
                            }
                        }

                        const selectors = [
                            '[data-testid="prompt-textarea"]',
                            'div.ProseMirror[contenteditable="true"]',
                            'div[contenteditable="true"][id*="prompt"]',
                            'form textarea',
                            'textarea',
                            'div[contenteditable="true"]'
                        ];

                        for (const selector of selectors) {
                            const matches = Array.from(document.querySelectorAll(selector));
                            const visible = matches.find(isVisible);
                            if (visible) {
                                return visible;
                            }
                        }

                        return null;
                    };

                    const insertIntoTextarea = (el, value) => {
                        const prototype =
                            el instanceof HTMLTextAreaElement
                                ? HTMLTextAreaElement.prototype
                                : HTMLInputElement.prototype;

                        const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
                        if (descriptor && descriptor.set) {
                            descriptor.set.call(el, value);
                        } else {
                            el.value = value;
                        }

                        el.dispatchEvent(new Event('input', { bubbles: true }));
                        el.dispatchEvent(new Event('change', { bubbles: true }));
                    };

                    const selectAll = (el) => {
                        if (el instanceof HTMLTextAreaElement || el instanceof HTMLInputElement) {
                            el.select();
                            return;
                        }

                        const selection = window.getSelection();
                        const range = document.createRange();
                        range.selectNodeContents(el);
                        selection.removeAllRanges();
                        selection.addRange(range);
                    };

                    const insertIntoComposer = async (el, value) => {
                        if (composerHasText(el, value)) {
                            return true;
                        }

                        try {
                            window.focus();
                            el.focus();
                            el.click();
                        } catch { }

                        if (
                            el instanceof HTMLTextAreaElement ||
                            el instanceof HTMLInputElement
                        ) {
                            insertIntoTextarea(
                                el,
                                replaceExisting ? value : ((el.value || '') + value)
                            );
                            await wait(50);
                            return composerHasText(el, value);
                        }

                        if (replaceExisting) {
                            selectAll(el);
                        } else {
                            try {
                                const selection = window.getSelection();
                                const range = document.createRange();
                                range.selectNodeContents(el);
                                range.collapse(false);
                                selection.removeAllRanges();
                                selection.addRange(range);
                            } catch { }
                        }

                        try {
                            document.execCommand('insertText', false, value);
                        } catch { }

                        await wait(80);
                        if (composerHasText(el, value)) {
                            return true;
                        }

                        if (replaceExisting) {
                            selectAll(el);
                        }
                        try {
                            const dataTransfer = new DataTransfer();
                            dataTransfer.setData('text/plain', value);
                            el.dispatchEvent(new ClipboardEvent('paste', {
                                clipboardData: dataTransfer,
                                bubbles: true,
                                cancelable: true
                            }));
                        } catch { }

                        await wait(80);
                        return composerHasText(el, value);
                    };

                    const isUsableSendButton = (button) => {
                        if (!button || !isVisible(button)) {
                            return false;
                        }

                        const testId = (button.getAttribute('data-testid') || '').toLowerCase();
                        const label = (button.getAttribute('aria-label') || '').toLowerCase();
                        if (testId.includes('stop') || label.includes('stop')) {
                            return false;
                        }

                        if (button.disabled || button.getAttribute('aria-disabled') === 'true') {
                            return false;
                        }

                        return true;
                    };

                    const findSendButton = () => {
                        const selectors = [
                            '#composer-submit-button',
                            'button[data-testid="send-button"]',
                            'button[data-testid="composer-send-button"]',
                            'button[aria-label="Send prompt"]',
                            'button[aria-label="Send message"]',
                            'button[aria-label="Send"]'
                        ];

                        for (const selector of selectors) {
                            const button = document.querySelector(selector);
                            if (isUsableSendButton(button)) {
                                return button;
                            }
                        }

                        const form = document.querySelector('form');
                        if (form) {
                            const submit = form.querySelector('button[type="submit"]');
                            if (isUsableSendButton(submit)) {
                                return submit;
                            }
                        }

                        return null;
                    };

                    const pressEnter = (el) => {
                        const options = {
                            key: 'Enter',
                            code: 'Enter',
                            keyCode: 13,
                            which: 13,
                            bubbles: true,
                            cancelable: true
                        };

                        el.dispatchEvent(new KeyboardEvent('keydown', options));
                        el.dispatchEvent(new KeyboardEvent('keypress', options));
                        el.dispatchEvent(new KeyboardEvent('keyup', options));
                    };

                    try {
                        let input = null;
                        for (let i = 0; i < 10; i++) {
                            input = findComposer();
                            if (input && input.getAttribute('contenteditable') !== 'false') {
                                break;
                            }

                            await wait(100);
                        }

                        if (!input) {
                            return JSON.stringify({
                                success: false,
                                reason: 'input-not-found'
                            });
                        }

                        for (let i = 0; i < 4; i++) {
                            if (composerHasText(input, text)) {
                                break;
                            }

                            await insertIntoComposer(input, text);
                            await wait(80);
                        }

                        if (!composerHasText(input, text)) {
                            return JSON.stringify({
                                success: false,
                                reason: 'insert-failed'
                            });
                        }

                        if (!shouldSend) {
                            return JSON.stringify({
                                success: true,
                                method: 'draft'
                            });
                        }

                        for (let i = 0; i < 20; i++) {
                            const sendButton = findSendButton();
                            if (sendButton) {
                                sendButton.click();
                                return JSON.stringify({
                                    success: true,
                                    method: 'button'
                                });
                            }

                            await wait(80);
                        }

                        input.focus();
                        pressEnter(input);

                        return JSON.stringify({
                            success: true,
                            method: 'enter'
                        });
                    } catch (error) {
                        return JSON.stringify({
                            success: false,
                            reason: String(error)
                        });
                    }
                })()
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

        private void OpacitySlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            double percent =
                Math.Clamp(
                    e.NewValue,
                    10,
                    100
                );

            if (OpacityValueText != null)
            {
                OpacityValueText.Text =
                    $"{Math.Round(percent)}%";
            }

            ApplyWindowOpacity(percent);
        }

        private void MicrophoneCapture_AudioAvailable(
            byte[] buffer,
            NAudio.Wave.WaveFormat format
        )
        {
            try
            {
                byte[] converted =
                    AudioConverter.To16KhzMonoPcm16(
                        buffer,
                        format
                    );

                double rms =
                    CalculateRms(
                        converted
                    );

                const double silenceThreshold =
                    0.008;

                bool containsAudio =
                    rms >= silenceThreshold;

                if (containsAudio)
                {
                    _lastMicrophoneSpeechTime =
                        DateTime.UtcNow;
                }
                else
                {
                    ScheduleMicrophoneFinalization();
                    return;
                }

                _microphoneLiveAudioBuffer.Add(
                    converted
                );

                if (
                    !_microphoneLiveAudioBuffer.HasChunkReady()
                )
                {
                    return;
                }

                byte[] chunk =
                    _microphoneLiveAudioBuffer.TakeChunk();

                if (
                    chunk.Length == 0
                )
                {
                    return;
                }

                QueueMicrophoneWhisper(
                    chunk
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Microphone audio processing failed: {ex}"
                );
            }
        }

        private void QueueMicrophoneWhisper(
    byte[] audio
)
        {
            if (
                audio == null ||
                audio.Length == 0
            )
            {
                return;
            }


            bool shouldStart =
                false;


            lock (_microphoneWhisperQueueLock)
            {
                _pendingMicrophoneAudio =
                    audio;


                if (!_microphoneWhisperProcessing)
                {
                    _microphoneWhisperProcessing =
                        true;

                    shouldStart =
                        true;
                }
            }


            if (shouldStart)
            {
                _ =
                    ProcessMicrophoneWhisperQueueAsync();
            }
        }

        private async Task ProcessMicrophoneWhisperQueueAsync()
        {
            while (true)
            {
                byte[]? audio;


                lock (_microphoneWhisperQueueLock)
                {
                    audio =
                        _pendingMicrophoneAudio;


                    _pendingMicrophoneAudio =
                        null;


                    if (audio == null)
                    {
                        _microphoneWhisperProcessing =
                            false;

                        return;
                    }
                }


                await TranscribeMicrophoneWhisperAsync(
                    audio
                );
            }
        }

        private async Task TranscribeMicrophoneWhisperAsync(
            byte[] audio
        )
        {
            if (
                _whisperService == null ||
                audio == null ||
                audio.Length == 0
            )
            {
                return;
            }

            try
            {
                if (_voiceActivityService != null)
                {
                    bool containsSpeech =
                        await _voiceActivityService
                            .ContainsSpeechAsync(
                                audio
                            );

                    if (!containsSpeech)
                    {
                        return;
                    }
                }

                string text =
                    await _whisperService
                        .TranscribeAsync(
                            audio
                        );

                text =
                    CleanWhisperText(
                        text
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        text
                    )
                )
                {
                    return;
                }

                text =
                    text.Trim();

                CaptionItem? latest =
                    _captionStore.GetLatest();

                bool sameSpeaker =
                    latest != null
                    &&
                    string.Equals(
                        latest.Speaker,
                        "Me",
                        StringComparison.OrdinalIgnoreCase
                    );

                string existingText =
                    sameSpeaker
                        ? latest!.Text
                        : string.Empty;

                string mergedText =
                    MergeWhisperCaption(
                        existingText,
                        text
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        mergedText
                    )
                )
                {
                    return;
                }

                if (
                    sameSpeaker
                    &&
                    string.Equals(
                        latest!.Text,
                        mergedText,
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }

                if (
                    sameSpeaker
                    &&
                    !string.IsNullOrWhiteSpace(
                        latest!.SegmentId
                    )
                )
                {
                    CaptionItem updatedCaption =
                        new CaptionItem
                        {
                            Speaker =
                                "Me",

                            Text =
                                mergedText,

                            Timestamp =
                                DateTime.Now.ToString(
                                    "HH:mm:ss"
                                ),

                            MeetingKey =
                                null,

                            SegmentId =
                                latest.SegmentId
                        };

                    _captionStore.Add(
                        updatedCaption
                    );
                }
                else
                {
                    CaptionItem newCaption =
                        new CaptionItem
                        {
                            Speaker =
                                "Me",

                            Text =
                                mergedText,

                            Timestamp =
                                DateTime.Now.ToString(
                                    "HH:mm:ss"
                                ),

                            MeetingKey =
                                null,

                            SegmentId =
                                Guid.NewGuid()
                                    .ToString("N")
                        };

                    _captionStore.Add(
                        newCaption
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Microphone Whisper failed: {ex}"
                );
            }
        }

        private void ScheduleMicrophoneFinalization()
        {
            if (_microphoneFinalizeScheduled)
            {
                return;
            }

            if (
                _microphoneLiveAudioBuffer.Length <= 0
            )
            {
                return;
            }

            _microphoneFinalizeScheduled =
                true;

            _ =
                FinalizeMicrophoneAfterSilenceAsync();
        }

        private async Task FinalizeMicrophoneAfterSilenceAsync()
        {
            try
            {
                await Task.Delay(
                    _speechEndDelay
                );

                TimeSpan silenceDuration =
                    DateTime.UtcNow -
                    _lastMicrophoneSpeechTime;

                if (
                    silenceDuration <
                    _speechEndDelay
                )
                {
                    return;
                }

                byte[] remainingAudio =
                    _microphoneLiveAudioBuffer
                        .TakeRemaining();

                if (
                    remainingAudio.Length > 0 &&
                    _whisperService != null
                )
                {
                    string text =
                        await _whisperService
                            .TranscribeAsync(
                                remainingAudio
                            );

                    text =
                        CleanWhisperText(
                            text
                        );

                    if (
                        !string.IsNullOrWhiteSpace(
                            text
                        )
                    )
                    {
                        text =
                            text.Trim();

                        CaptionItem? latest =
                            _captionStore.GetLatest();

                        bool sameSpeaker =
                            latest != null
                            &&
                            string.Equals(
                                latest.Speaker,
                                "Me",
                                StringComparison.OrdinalIgnoreCase
                            );

                        string existingText =
                            sameSpeaker
                                ? latest!.Text
                                : string.Empty;

                        string mergedText =
                            MergeWhisperCaption(
                                existingText,
                                text
                            );

                        if (
                            !string.IsNullOrWhiteSpace(
                                mergedText
                            )
                        )
                        {
                            if (
                                sameSpeaker
                                &&
                                !string.IsNullOrWhiteSpace(
                                    latest!.SegmentId
                                )
                            )
                            {
                                if (
                                    !string.Equals(
                                        latest.Text,
                                        mergedText,
                                        StringComparison.Ordinal
                                    )
                                )
                                {
                                    CaptionItem updatedCaption =
                                        new CaptionItem
                                        {
                                            Speaker =
                                                "Me",

                                            Text =
                                                mergedText,

                                            Timestamp =
                                                DateTime.Now.ToString(
                                                    "HH:mm:ss"
                                                ),

                                            MeetingKey =
                                                null,

                                            SegmentId =
                                                latest.SegmentId
                                        };

                                    _captionStore.Add(
                                        updatedCaption
                                    );
                                }
                            }
                            else
                            {
                                CaptionItem newCaption =
                                    new CaptionItem
                                    {
                                        Speaker =
                                            "Me",

                                        Text =
                                            mergedText,

                                        Timestamp =
                                            DateTime.Now.ToString(
                                                "HH:mm:ss"
                                            ),

                                        MeetingKey =
                                            null,

                                        SegmentId =
                                            Guid.NewGuid()
                                                .ToString("N")
                                    };

                                _captionStore.Add(
                                    newCaption
                                );
                            }
                        }
                    }
                }

                _microphoneLiveAudioBuffer.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Microphone finalization failed: {ex}"
                );
            }
            finally
            {
                _microphoneFinalizeScheduled =
                    false;
            }
        }
    }
}