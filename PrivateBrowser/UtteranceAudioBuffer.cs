using System;
using System.IO;

namespace PrivateBrowser
{
    public sealed class UtteranceAudioBuffer
    {
        private readonly object _sync =
            new object();

        private readonly MemoryStream _buffer =
            new MemoryStream();


        public void Add(
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


            lock (_sync)
            {
                _buffer.Write(
                    audio,
                    0,
                    audio.Length
                );
            }
        }


        public byte[] Snapshot()
        {
            lock (_sync)
            {
                return _buffer.ToArray();
            }
        }


        public byte[] Take()
        {
            lock (_sync)
            {
                byte[] result =
                    _buffer.ToArray();

                _buffer.SetLength(
                    0
                );

                return result;
            }
        }


        public void Clear()
        {
            lock (_sync)
            {
                _buffer.SetLength(
                    0
                );
            }
        }


        public long Length
        {
            get
            {
                lock (_sync)
                {
                    return _buffer.Length;
                }
            }
        }
    }
}