using System;
using System.IO;

namespace PrivateBrowser
{
    public sealed class AudioChunkBuffer
    {
        private readonly object _sync =
            new object();

        private readonly MemoryStream _buffer =
            new MemoryStream();


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
                _buffer.Write(
                    data,
                    0,
                    data.Length
                );
            }
        }


        public byte[] Take()
        {
            lock (_sync)
            {
                if (_buffer.Length == 0)
                {
                    return Array.Empty<byte>();
                }

                byte[] data =
                    _buffer.ToArray();

                _buffer.SetLength(
                    0
                );

                return data;
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


        public void Clear()
        {
            lock (_sync)
            {
                _buffer.SetLength(
                    0
                );
            }
        }
    }
}