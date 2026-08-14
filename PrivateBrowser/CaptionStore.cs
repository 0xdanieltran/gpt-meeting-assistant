using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PrivateBrowser
{
    public class CaptionStore
    {
        private readonly object _lock =
            new object();

        private readonly List<CaptionItem> _history =
            new List<CaptionItem>();

        private CaptionItem? _latest;


        public event Action? Changed;


        // =========================================================
        // COMPATIBILITY
        // =========================================================

        public string? GetCurrentMeetingKey()
        {
            return null;
        }


        // =========================================================
        // LATEST
        // =========================================================

        public CaptionItem? GetLatest()
        {
            lock (_lock)
            {
                return _latest;
            }
        }


        // =========================================================
        // HISTORY
        // =========================================================

        public IReadOnlyList<CaptionItem> GetHistory()
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }


        // =========================================================
        // ADD / UPDATE CAPTION
        // =========================================================
        //
        // Use this when you already have a CaptionItem and want
        // to update an existing SegmentId or add a new item.
        // =========================================================

        public void Add(
            CaptionItem caption
        )
        {
            if (caption == null)
            {
                return;
            }


            if (
                string.IsNullOrWhiteSpace(
                    caption.Text
                )
            )
            {
                return;
            }


            caption.Text =
                Normalize(
                    caption.Text
                );


            if (
                string.IsNullOrWhiteSpace(
                    caption.Text
                )
            )
            {
                return;
            }


            bool changed =
                false;


            lock (_lock)
            {
                CaptionItem? existing =
                    null;


                // =================================================
                // UPDATE EXISTING SEGMENT
                // =================================================

                if (
                    !string.IsNullOrWhiteSpace(
                        caption.SegmentId
                    )
                )
                {
                    existing =
                        _history.FirstOrDefault(
                            item =>
                                string.Equals(
                                    item.SegmentId,
                                    caption.SegmentId,
                                    StringComparison.Ordinal
                                )
                        );
                }


                if (existing != null)
                {
                    bool textChanged =
                        !string.Equals(
                            existing.Text,
                            caption.Text,
                            StringComparison.Ordinal
                        );


                    bool speakerChanged =
                        !string.Equals(
                            existing.Speaker,
                            caption.Speaker,
                            StringComparison.OrdinalIgnoreCase
                        );


                    existing.Text =
                        caption.Text;

                    existing.Speaker =
                        caption.Speaker;

                    existing.Timestamp =
                        caption.Timestamp;


                    _latest =
                        existing;


                    if (
                        textChanged ||
                        speakerChanged
                    )
                    {
                        changed =
                            true;
                    }
                }
                else
                {
                    // =================================================
                    // EXACT DUPLICATE CHECK
                    // =================================================

                    CaptionItem? previous =
                        _history.LastOrDefault();


                    bool duplicate =
                        previous != null
                        &&
                        string.Equals(
                            previous.Text,
                            caption.Text,
                            StringComparison.Ordinal
                        )
                        &&
                        string.Equals(
                            previous.Speaker,
                            caption.Speaker,
                            StringComparison.OrdinalIgnoreCase
                        );


                    if (duplicate)
                    {
                        _latest =
                            previous;
                    }
                    else
                    {
                        _history.Add(
                            caption
                        );


                        _latest =
                            caption;


                        changed =
                            true;


                        TrimHistoryIfNeeded();
                    }
                }
            }


            if (changed)
            {
                Changed?.Invoke();
            }
        }


        // =========================================================
        // APPEND STREAMING TEXT
        // =========================================================
        //
        // This is the method to use for local Whisper streaming.
        //
        // Same speaker:
        //     append to the latest paragraph.
        //
        // Different speaker:
        //     create a new paragraph.
        // =========================================================

        public void Append(
            string speaker,
            string text
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    text
                )
            )
            {
                return;
            }


            speaker =
                NormalizeSpeaker(
                    speaker
                );


            text =
                Normalize(
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


            bool changed =
                false;


            lock (_lock)
            {
                CaptionItem? previous =
                    _history.LastOrDefault();


                // =================================================
                // SAME SPEAKER
                // =================================================

                if (
                    previous != null
                    &&
                    string.Equals(
                        NormalizeSpeaker(
                            previous.Speaker
                        ),
                        speaker,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    string newPortion =
                        RemoveDuplicateBoundaryText(
                            previous.Text,
                            text
                        );


                    if (
                        string.IsNullOrWhiteSpace(
                            newPortion
                        )
                    )
                    {
                        _latest =
                            previous;

                        return;
                    }


                    previous.Text =
                        Normalize(
                            previous.Text +
                            " " +
                            newPortion
                        );


                    previous.Timestamp =
                        DateTime.Now.ToString(
                            "HH:mm:ss"
                        );


                    _latest =
                        previous;


                    changed =
                        true;
                }
                else
                {
                    // =================================================
                    // DIFFERENT SPEAKER / FIRST CAPTION
                    // =================================================

                    CaptionItem caption =
                        new CaptionItem
                        {
                            Speaker =
                                speaker,

                            Text =
                                text,

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


                    _history.Add(
                        caption
                    );


                    _latest =
                        caption;


                    changed =
                        true;


                    TrimHistoryIfNeeded();
                }
            }


            if (changed)
            {
                Changed?.Invoke();
            }
        }


        // =========================================================
        // LATEST TEXT
        // =========================================================

        public string GetLatestText()
        {
            lock (_lock)
            {
                return _latest?.Text
                    ?? string.Empty;
            }
        }


        // =========================================================
        // FULL TRANSCRIPT
        // =========================================================

        public string GetTranscript()
        {
            lock (_lock)
            {
                StringBuilder builder =
                    new StringBuilder();


                foreach (
                    CaptionItem item
                    in _history
                )
                {
                    if (
                        builder.Length >
                        0
                    )
                    {
                        builder.AppendLine();
                        builder.AppendLine();
                    }


                    if (
                        !string.IsNullOrWhiteSpace(
                            item.Speaker
                        )
                    )
                    {
                        builder.Append(
                            item.Speaker
                        );

                        builder.AppendLine();
                    }


                    builder.Append(
                        item.Text
                    );
                }


                return builder.ToString();
            }
        }


        // =========================================================
        // CLEAR CURRENT SESSION
        // =========================================================

        public void Clear()
        {
            bool changed;


            lock (_lock)
            {
                changed =
                    _history.Count > 0 ||
                    _latest != null;


                _history.Clear();

                _latest =
                    null;
            }


            if (changed)
            {
                Changed?.Invoke();
            }
        }


        // =========================================================
        // REMOVE DUPLICATED CHUNK BOUNDARY
        // =========================================================
        //
        // Example:
        //
        // Previous:
        // "We are living in Australia."
        //
        // Incoming:
        // "Australia. We are doing freelancing."
        //
        // Result:
        // "We are doing freelancing."
        // =========================================================

        private static string RemoveDuplicateBoundaryText(
            string existing,
            string incoming
        )
        {
            existing =
                Normalize(
                    existing
                );

            incoming =
                Normalize(
                    incoming
                );


            if (
                string.IsNullOrWhiteSpace(
                    existing
                )
            )
            {
                return incoming;
            }


            if (
                string.IsNullOrWhiteSpace(
                    incoming
                )
            )
            {
                return string.Empty;
            }


            if (
                string.Equals(
                    existing,
                    incoming,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return string.Empty;
            }


            if (
                existing.EndsWith(
                    incoming,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return string.Empty;
            }


            string[] existingWords =
                existing.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );


            string[] incomingWords =
                incoming.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );


            int maximumOverlap =
                Math.Min(
                    existingWords.Length,
                    incomingWords.Length
                );


            // Limit overlap checking so this stays lightweight.
            maximumOverlap =
                Math.Min(
                    maximumOverlap,
                    20
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
                        NormalizeComparisonWord(
                            existingWords[
                                existingWords.Length -
                                overlap +
                                i
                            ]
                        );


                    string newWord =
                        NormalizeComparisonWord(
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


                if (matches)
                {
                    if (
                        overlap >=
                        incomingWords.Length
                    )
                    {
                        return string.Empty;
                    }


                    return string.Join(
                        " ",
                        incomingWords.Skip(
                            overlap
                        )
                    );
                }
            }


            return incoming;
        }


        // =========================================================
        // NORMALIZE COMPARISON WORD
        // =========================================================

        private static string NormalizeComparisonWord(
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


        // =========================================================
        // NORMALIZE SPEAKER
        // =========================================================

        private static string NormalizeSpeaker(
            string? speaker
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    speaker
                )
            )
            {
                return "Interviwer";
            }


            return speaker.Trim();
        }


        // =========================================================
        // LIMIT MEMORY USAGE
        // =========================================================

        private void TrimHistoryIfNeeded()
        {
            if (
                _history.Count <=
                5000
            )
            {
                return;
            }


            _history.RemoveRange(
                0,
                _history.Count - 5000
            );
        }


        // =========================================================
        // NORMALIZE WHITESPACE
        // =========================================================

        private static string Normalize(
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


            return string.Join(
                " ",
                text.Split(
                    new[]
                    {
                        ' ',
                        '\r',
                        '\n',
                        '\t'
                    },
                    StringSplitOptions
                        .RemoveEmptyEntries
                )
            ).Trim();
        }
    }
}