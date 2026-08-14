using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.IO;

namespace PrivateBrowser
{
    public sealed class SystemAudioCaptureService : IDisposable
    {
        private WasapiLoopbackCapture? _capture;

        public event Action<byte[], WaveFormat>? AudioAvailable;

        public bool IsRunning =>
            _capture != null;


        public void Start()
        {

            if (_capture != null)
            {
                return;
            }

            using MMDeviceEnumerator enumerator =
                new MMDeviceEnumerator();

            MMDevice device =
                enumerator.GetDefaultAudioEndpoint(
                    DataFlow.Render,
                    Role.Multimedia
                );

            MMDevice communications =
                enumerator.GetDefaultAudioEndpoint(
                    DataFlow.Render,
                    Role.Communications
                );

            _capture =
                new WasapiLoopbackCapture(
                    device
                );

            _capture.DataAvailable +=
                Capture_DataAvailable;

            _capture.RecordingStopped +=
                Capture_RecordingStopped;

            _capture.StartRecording();
        }


        public void Stop()
        {
            if (_capture == null)
            {
                return;
            }


            try
            {
                _capture.StopRecording();
            }
            catch
            {
                CleanupCapture();
            }
        }


        private void Capture_DataAvailable(
            object? sender,
            WaveInEventArgs e
        )
        {
            string logFile =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "audio-debug.log"
                );

            if (
                e.BytesRecorded <= 0 ||
                _capture == null
            )
            {
                return;
            }

            byte[] buffer =
                new byte[e.BytesRecorded];

            Buffer.BlockCopy(
                e.Buffer,
                0,
                buffer,
                0,
                e.BytesRecorded
            );

            AudioAvailable?.Invoke(
                buffer,
                _capture.WaveFormat
            );
        }


        private void Capture_RecordingStopped(
            object? sender,
            StoppedEventArgs e
        )
        {
            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Audio capture stopped: {e.Exception}"
                );
            }


            CleanupCapture();
        }


        private void CleanupCapture()
        {
            if (_capture == null)
            {
                return;
            }


            _capture.DataAvailable -=
                Capture_DataAvailable;


            _capture.RecordingStopped -=
                Capture_RecordingStopped;


            _capture.Dispose();

            _capture =
                null;
        }


        public void Dispose()
        {
            Stop();

            CleanupCapture();
        }
    }
}