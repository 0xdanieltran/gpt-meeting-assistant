using System;
using NAudio.Wave;

namespace PrivateBrowser
{
    public sealed class MicrophoneCaptureService : IDisposable
    {
        private WaveInEvent? _waveIn;

        private bool _started =
            false;


        public event Action<
            byte[],
            WaveFormat
        >? AudioAvailable;


        public void Start()
        {
            if (_started)
            {
                return;
            }


            _waveIn =
                new WaveInEvent
                {
                    // Windows default microphone.
                    DeviceNumber = 0,

                    WaveFormat =
                        new WaveFormat(
                            16000,
                            16,
                            1
                        ),

                    BufferMilliseconds =
                        100
                };


            _waveIn.DataAvailable +=
                WaveIn_DataAvailable;


            _waveIn.RecordingStopped +=
                WaveIn_RecordingStopped;


            _waveIn.StartRecording();


            _started =
                true;


            System.Diagnostics.Debug.WriteLine(
                "Microphone capture started."
            );
        }


        private void WaveIn_DataAvailable(
            object? sender,
            WaveInEventArgs e
        )
        {
            if (
                e.BytesRecorded <= 0
            )
            {
                return;
            }


            byte[] copy =
                new byte[
                    e.BytesRecorded
                ];


            Buffer.BlockCopy(
                e.Buffer,
                0,
                copy,
                0,
                e.BytesRecorded
            );


            AudioAvailable?.Invoke(
                copy,
                _waveIn!.WaveFormat
            );
        }


        private void WaveIn_RecordingStopped(
            object? sender,
            StoppedEventArgs e
        )
        {
            _started =
                false;


            if (
                e.Exception != null
            )
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Microphone capture stopped with error: {e.Exception}"
                );
            }
        }


        public void Stop()
        {
            if (!_started)
            {
                return;
            }


            try
            {
                _waveIn?.StopRecording();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Microphone stop failed: {ex}"
                );
            }


            _started =
                false;
        }


        public void Dispose()
        {
            Stop();


            if (_waveIn != null)
            {
                _waveIn.DataAvailable -=
                    WaveIn_DataAvailable;

                _waveIn.RecordingStopped -=
                    WaveIn_RecordingStopped;

                _waveIn.Dispose();

                _waveIn =
                    null;
            }
        }
    }
}