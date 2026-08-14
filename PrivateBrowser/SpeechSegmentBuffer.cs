using System;
using System.IO;

namespace PrivateBrowser
{
    public sealed class SpeechSegmentBuffer
    {
        private readonly object _sync =
            new object();

        private readonly MemoryStream _speechBuffer =
            new MemoryStream();

        private bool _isSpeaking = false;

        private int _silentChunkCount = 0;


        // 16 kHz mono PCM16
        private const int BytesPerSecond =
            16000 * 2;


        // Each incoming chunk is about 1 second
        // in your current pipeline.
        private const int SilenceChunksToFinish =
            1;


        // Prevent extremely long segments.
        private const int MaxSegmentSeconds =
            15;


        public byte[]? AddChunk(
            byte[] chunk,
            bool containsSpeech
        )
        {
            if (
                chunk == null ||
                chunk.Length == 0
            )
            {
                return null;
            }


            lock (_sync)
            {
                if (containsSpeech)
                {
                    _isSpeaking =
                        true;

                    _silentChunkCount =
                        0;

                    _speechBuffer.Write(
                        chunk,
                        0,
                        chunk.Length
                    );
                }
                else
                {
                    if (!_isSpeaking)
                    {
                        return null;
                    }

                    _silentChunkCount++;

                    // Keep a small amount of trailing silence.
                    _speechBuffer.Write(
                        chunk,
                        0,
                        chunk.Length
                    );
                }


                // Force-finish if segment gets too long.
                if (
                    _speechBuffer.Length >=
                    BytesPerSecond *
                    MaxSegmentSeconds
                )
                {
                    return FinishSegment();
                }


                // Finish after silence.
                if (
                    _isSpeaking &&
                    _silentChunkCount >=
                    SilenceChunksToFinish
                )
                {
                    return FinishSegment();
                }


                return null;
            }
        }


        private byte[] FinishSegment()
        {
            byte[] result =
                _speechBuffer.ToArray();

            _speechBuffer.SetLength(
                0
            );

            _isSpeaking =
                false;

            _silentChunkCount =
                0;

            return result;
        }


        public void Clear()
        {
            lock (_sync)
            {
                _speechBuffer.SetLength(
                    0
                );

                _isSpeaking =
                    false;

                _silentChunkCount =
                    0;
            }
        }
    }
}