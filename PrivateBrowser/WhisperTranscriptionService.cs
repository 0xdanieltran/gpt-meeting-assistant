using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace PrivateBrowser
{
    public sealed class WhisperTranscriptionService : IDisposable
    {
        private readonly WhisperFactory _factory;
        private readonly WhisperProcessor _processor;

        private readonly SemaphoreSlim _transcriptionLock =
            new SemaphoreSlim(1, 1);

        private bool _disposed;


        public WhisperTranscriptionService(
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
                    "Whisper model path is required.",
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
                    "Whisper model was not found.",
                    modelPath
                );
            }

            _factory =
                WhisperFactory.FromPath(
                    modelPath
                );

            _processor =
                _factory
                    .CreateBuilder()
                    .WithLanguage("en")
                    .Build();
        }


        public async Task<string> TranscribeAsync(
            byte[] pcm16Audio,
            CancellationToken cancellationToken =
                default
        )
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(
                        WhisperTranscriptionService
                    )
                );
            }

            if (
                pcm16Audio == null ||
                pcm16Audio.Length == 0
            )
            {
                return string.Empty;
            }

            await _transcriptionLock.WaitAsync(
                cancellationToken
            );

            try
            {
                using MemoryStream wavStream =
                    CreateWavStream(
                        pcm16Audio
                    );

                StringBuilder transcript =
                    new StringBuilder();

                await foreach (
                    var segment in
                    _processor.ProcessAsync(
                        wavStream,
                        cancellationToken
                    )
                )
                {
                    if (
                        !string.IsNullOrWhiteSpace(
                            segment.Text
                        )
                    )
                    {
                        if (
                            transcript.Length > 0
                        )
                        {
                            transcript.Append(
                                ' '
                            );
                        }

                        transcript.Append(
                            segment.Text.Trim()
                        );
                    }
                }

                return transcript
                    .ToString()
                    .Trim();
            }
            finally
            {
                _transcriptionLock.Release();
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
                        new NonClosingStreamWrapper(stream),
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

            stream.Position = 0;

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
            _transcriptionLock.Dispose();
        }
    }

    internal sealed class NonClosingStreamWrapper : Stream
    {
        private readonly Stream _inner;

        public NonClosingStreamWrapper(
            Stream inner
        )
        {
            _inner = inner;
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
            get => _inner.Position;
            set => _inner.Position = value;
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
            // Intentionally do NOT dispose _inner.
            base.Dispose(disposing);
        }
    }
}