using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfKey = System.Windows.Input.Key;

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


            // =========================================================
            // CURRENT IN-MEMORY CAPTION HISTORY
            // =========================================================

            if (history != null)
            {
                foreach (
                    CaptionItem item
                    in history
                )
                {
                    string speaker =
                        string.IsNullOrWhiteSpace(
                            item.Speaker
                        )
                            ? "Interviewer"
                            : item.Speaker;


                    Paragraph paragraph =
                        CreateTranscriptParagraph(
                            speaker,
                            item.Text
                        );


                    TranscriptDocument.Blocks.Add(
                        paragraph
                    );
                }
            }


            // =========================================================
            // NOTHING YET
            // =========================================================

            if (
                TranscriptDocument.Blocks.Count ==
                0
            )
            {
                Paragraph empty =
                    new Paragraph(
                        new Run(
                            "Waiting for live captions..."
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
            }


            TranscriptText.ScrollToEnd();
        }


        private Paragraph CreateTranscriptParagraph(
            string speaker,
            string text
        )
        {
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


            // =========================================================
            // SPEAKER
            // =========================================================

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


            paragraph.Inlines.Add(
                new LineBreak()
            );


            // =========================================================
            // TEXT
            // =========================================================

            Run captionRun =
                new Run(
                    text
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


            return paragraph;
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
                Browser.Focus();
            }
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

            WpfMessageBox.Show(
                $"{displayName} could not be registered.\n\n" +
                "Another application may already be using this global shortcut.\n" +
                $"Windows error: {error}",
                "PrivateBrowser Hotkey Error",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Warning
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
                    WpfMessageBox.Show(
                        "There is no latest caption to send.",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Information
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
                    WpfMessageBox.Show(
                        "The latest caption is empty.",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Information
                    );

                    return;
                }

                bool copied =
                    await TrySetClipboardTextAsync(
                        text
                    );

                if (!copied)
                {
                    WpfMessageBox.Show(
                        "Could not copy the latest caption.",
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
                WpfMessageBox.Show(
                    "Please open ChatGPT first.",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information
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