using System;
using System.Collections.Generic;

namespace PrivateBrowser
{
    public sealed class LiveAudioBuffer
    {
        private readonly object _sync =
            new object();

        private readonly List<byte> _buffer =
            new List<byte>();

        private readonly int _previewBytes =
            (int)(
                BytesPerSecond *
                1.4
            );

        private bool _previewSent =
            false;

        // 16 kHz mono PCM16
        private const int BytesPerSecond =
            16000 * 2;


        // Give Whisper ~3 seconds of context.
        private readonly int _chunkBytes =
            BytesPerSecond * 3;


        // Advance approximately 1 second each time.
        // Therefore retain ~2 seconds as context.
        private readonly int _keepBytes =
            BytesPerSecond * 2;


        public void Add(
            byte[] data
        )
        {
            if (
                data == null ||
                data.Length == 0
            )
            {
                return;
            }

            lock (_sync)
            {
                _buffer.AddRange(
                    data
                );
            }
        }


        public bool HasChunkReady()
        {
            lock (_sync)
            {
                return
                    _buffer.Count >=
                    _chunkBytes;
            }
        }


        public byte[] TakeChunk()
        {
            lock (_sync)
            {
                if (
                    _buffer.Count <
                    _chunkBytes
                )
                {
                    return Array.Empty<byte>();
                }

                byte[] chunk =
                    _buffer
                        .GetRange(
                            0,
                            _chunkBytes
                        )
                        .ToArray();

                // 3 sec was processed.
                // Remove only 1 sec so that 2 sec remains
                // as context for the next transcription.
                int removeCount =
                    _chunkBytes -
                    _keepBytes;

                _buffer.RemoveRange(
                    0,
                    removeCount
                );

                return chunk;
            }
        }


        public byte[] TakeRemaining()
        {
            lock (_sync)
            {
                if (_buffer.Count == 0)
                {
                    return Array.Empty<byte>();
                }

                byte[] result =
                    _buffer.ToArray();

                _buffer.Clear();

                return result;
            }
        }


        public void Clear()
        {
            lock (_sync)
            {
                _buffer.Clear();

                _previewSent =
                    false;
            }
        }


        public int Length
        {
            get
            {
                lock (_sync)
                {
                    return _buffer.Count;
                }
            }
        }

        public bool HasPreviewReady()
        {
            lock (_sync)
            {
                return
                    !_previewSent
                    &&
                    _buffer.Count >=
                    _previewBytes;
            }
        }

        public byte[] TakePreview()
        {
            lock (_sync)
            {
                if (
                    _previewSent
                    ||
                    _buffer.Count <
                    _previewBytes
                )
                {
                    return Array.Empty<byte>();
                }

                _previewSent =
                    true;

                return _buffer
                    .GetRange(
                        0,
                        _previewBytes
                    )
                    .ToArray();
            }
        }
    }
}