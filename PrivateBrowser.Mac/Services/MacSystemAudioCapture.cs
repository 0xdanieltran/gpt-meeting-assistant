namespace PrivateBrowser.Mac.Services
{
    /// <summary>
    /// System-audio loopback on macOS requires Screen Recording permission
    /// and ScreenCaptureKit. Capture is started from the Mac host; PCM16 16 kHz
    /// frames should be pushed into <see cref="AudioAvailable"/>.
    /// </summary>
    public sealed class MacSystemAudioCapture
    {
        public event Action<byte[]>? AudioAvailable;

        public void Start()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                "Grant Screen Recording to PrivateBrowser so meeting audio can be transcribed."
            );
        }

        public void Stop()
        {
        }

        public void PushPcm16(
            byte[] pcm16
        )
        {
            if (pcm16 is { Length: > 0 })
            {
                AudioAvailable?.Invoke(pcm16);
            }
        }
    }
}
