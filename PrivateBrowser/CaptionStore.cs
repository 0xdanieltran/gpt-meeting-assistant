using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PrivateBrowser
{
    public class CaptionStore
    {
        private readonly object _lock = new();

        private readonly List<CaptionItem> _history = new();

        private CaptionItem? _latest;

        private string? _currentMeetingKey;

        public event Action? Changed;


        public string? GetCurrentMeetingKey()
        {
            lock (_lock)
            {
                return _currentMeetingKey;
            }
        }


        public CaptionItem? GetLatest()
        {
            lock (_lock)
            {
                return _latest;
            }
        }


        public IReadOnlyList<CaptionItem> GetHistory()
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }


        public void Add(CaptionItem caption)
        {
            if (caption == null)
                return;

            if (string.IsNullOrWhiteSpace(caption.Text))
                return;


            caption.Text =
                Normalize(caption.Text);


            if (string.IsNullOrWhiteSpace(caption.Text))
                return;


            bool changed =
                false;


            lock (_lock)
            {
                // =====================================================
                // MEETING CHANGE
                // =====================================================

                if (
                    !string.IsNullOrWhiteSpace(
                        caption.MeetingKey
                    )
                )
                {
                    if (
                        !string.IsNullOrWhiteSpace(
                            _currentMeetingKey
                        )
                        &&
                        !string.Equals(
                            _currentMeetingKey,
                            caption.MeetingKey,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        _history.Clear();

                        _latest =
                            null;

                        changed =
                            true;
                    }


                    _currentMeetingKey =
                        caption.MeetingKey;
                }


                // =====================================================
                // UPDATE EXISTING SEGMENT
                // =====================================================

                CaptionItem? existing =
                    null;


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
                                &&
                                string.Equals(
                                    item.MeetingKey,
                                    caption.MeetingKey,
                                    StringComparison.OrdinalIgnoreCase
                                )
                        );
                }


                if (
                    existing !=
                    null
                )
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
                            StringComparison.Ordinal
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
                            StringComparison.Ordinal
                        );


                    if (
                        duplicate
                    )
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


                        if (
                            _history.Count >
                            5000
                        )
                        {
                            _history.RemoveRange(
                                0,
                                _history.Count - 5000
                            );
                        }
                    }
                }
            }


            // IMPORTANT:
            // Invoke outside _lock.

            if (
                changed
            )
            {
                Changed?.Invoke();
            }
        }


        public string GetLatestText()
        {
            lock (_lock)
            {
                return _latest?.Text
                    ?? string.Empty;
            }
        }


        public string GetTranscript()
        {
            lock (_lock)
            {
                var builder =
                    new StringBuilder();


                foreach (
                    CaptionItem item
                    in _history
                )
                {
                    if (builder.Length > 0)
                    {
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

                        builder.Append(": ");
                    }


                    builder.Append(
                        item.Text
                    );
                }


                return builder.ToString();
            }
        }


        public void Clear()
        {
            bool changed;


            lock (_lock)
            {
                changed =
                    _history.Count > 0 ||
                    _latest != null ||
                    _currentMeetingKey != null;


                _history.Clear();

                _latest =
                    null;

                _currentMeetingKey =
                    null;
            }


            if (
                changed
            )
            {
                Changed?.Invoke();
            }
        }


        private static string Normalize(
            string text
        )
        {
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
                    StringSplitOptions.RemoveEmptyEntries
                )
            ).Trim();
        }
    }
}