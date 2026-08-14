using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace PrivateBrowser
{
    public sealed class VoiceActivityService : IDisposable
    {
        private readonly WhisperVadFactory _factory;
        private readonly WhisperVadProcessor _processor;

        private readonly SemaphoreSlim _vadLock =
            new SemaphoreSlim(1, 1);

        private bool _disposed;


        public VoiceActivityService(
            string modelPath
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    modelPath
                )
            )
            {
                throw new ArgumentException(
                    "VAD model path is required.",
                    nameof(modelPath)
                );
            }

            if (
                !File.Exists(
                    modelPath
                )
            )
            {
                throw new FileNotFoundException(
                    "VAD model was not found.",
                    modelPath
                );
            }

            _factory =
                WhisperVadFactory.FromPath(
                    modelPath
                );

            _processor =
                _factory
                    .CreateBuilder()
                    .WithThreshold(
                        0.5f
                    )
                    .Build();
        }


        public async Task<bool> ContainsSpeechAsync(
            byte[] pcm16Audio,
            CancellationToken cancellationToken =
                default
        )
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(
                        VoiceActivityService
                    )
                );
            }

            if (
                pcm16Audio == null ||
                pcm16Audio.Length == 0
            )
            {
                return false;
            }

            await _vadLock.WaitAsync(
                cancellationToken
            );

            try
            {
                using MemoryStream wavStream =
                    CreateWavStream(
                        pcm16Audio
                    );

                var segments =
                    await _processor
                        .DetectSpeechAsync(
                            wavStream
                        );

                return
                    segments.Count > 0;
            }
            finally
            {
                _vadLock.Release();
            }
        }


        private static MemoryStream CreateWavStream(
            byte[] pcm16Audio
        )
        {
            MemoryStream stream =
                new MemoryStream();

            NAudio.Wave.WaveFormat waveFormat =
                new NAudio.Wave.WaveFormat(
                    16000,
                    16,
                    1
                );

            using (
                NAudio.Wave.WaveFileWriter writer =
                    new NAudio.Wave.WaveFileWriter(
                        new VadNonClosingStreamWrapper(
                            stream
                        ),
                        waveFormat
                    )
            )
            {
                writer.Write(
                    pcm16Audio,
                    0,
                    pcm16Audio.Length
                );

                writer.Flush();
            }

            stream.Position =
                0;

            return stream;
        }


        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed =
                true;

            _processor.Dispose();
            _factory.Dispose();
            _vadLock.Dispose();
        }
    }


    internal sealed class VadNonClosingStreamWrapper : Stream
    {
        private readonly Stream _inner;


        public VadNonClosingStreamWrapper(
            Stream inner
        )
        {
            _inner =
                inner;
        }


        public override bool CanRead =>
            _inner.CanRead;

        public override bool CanSeek =>
            _inner.CanSeek;

        public override bool CanWrite =>
            _inner.CanWrite;

        public override long Length =>
            _inner.Length;


        public override long Position
        {
            get =>
                _inner.Position;

            set =>
                _inner.Position =
                    value;
        }


        public override void Flush()
        {
            _inner.Flush();
        }


        public override int Read(
            byte[] buffer,
            int offset,
            int count
        )
        {
            return _inner.Read(
                buffer,
                offset,
                count
            );
        }


        public override long Seek(
            long offset,
            SeekOrigin origin
        )
        {
            return _inner.Seek(
                offset,
                origin
            );
        }


        public override void SetLength(
            long value
        )
        {
            _inner.SetLength(
                value
            );
        }


        public override void Write(
            byte[] buffer,
            int offset,
            int count
        )
        {
            _inner.Write(
                buffer,
                offset,
                count
            );
        }


        protected override void Dispose(
            bool disposing
        )
        {
            // Keep the underlying MemoryStream open.
            base.Dispose(
                disposing
            );
        }
    }
}