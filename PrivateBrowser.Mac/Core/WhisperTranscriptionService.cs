using System.Text;
using Whisper.net;

namespace PrivateBrowser.Mac
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
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException(
                    "Whisper model path is required.",
                    nameof(modelPath)
                );
            }

            if (!File.Exists(modelPath))
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
            CancellationToken cancellationToken = default
        )
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this
            );

            if (pcm16Audio == null || pcm16Audio.Length == 0)
            {
                return string.Empty;
            }

            await _transcriptionLock.WaitAsync(
                cancellationToken
            );

            try
            {
                using MemoryStream wavStream =
                    WavWriter.CreatePcm16Mono16kStream(
                        pcm16Audio
                    );

                StringBuilder transcript =
                    new StringBuilder();

                await foreach (
                    var segment in _processor.ProcessAsync(
                        wavStream,
                        cancellationToken
                    )
                )
                {
                    if (!string.IsNullOrWhiteSpace(segment.Text))
                    {
                        if (transcript.Length > 0)
                        {
                            transcript.Append(' ');
                        }

                        transcript.Append(
                            segment.Text.Trim()
                        );
                    }
                }

                return transcript.ToString().Trim();
            }
            finally
            {
                _transcriptionLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _processor.Dispose();
            _factory.Dispose();
            _transcriptionLock.Dispose();
        }
    }
}
