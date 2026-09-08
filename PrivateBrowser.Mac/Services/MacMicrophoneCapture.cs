using System.Runtime.InteropServices;

namespace PrivateBrowser.Mac.Services
{
    public sealed class MacMicrophoneCapture : IDisposable
    {
        public event Action<byte[]>? AudioAvailable;

        private IntPtr _queue;
        private GCHandle _callbackHandle;
        private bool _running;

        public void Start()
        {
            if (!OperatingSystem.IsMacOS() || _running)
            {
                return;
            }

            AudioStreamBasicDescription format = new()
            {
                mSampleRate = 16000,
                mFormatID = 0x6C70636D, // 'lpcm'
                mFormatFlags = 12, // signed integer, packed
                mBytesPerPacket = 2,
                mFramesPerPacket = 1,
                mBytesPerFrame = 2,
                mChannelsPerFrame = 1,
                mBitsPerSample = 16
            };

            AudioQueueInputCallback callback = OnInput;
            _callbackHandle = GCHandle.Alloc(callback);

            int status = AudioQueueNewInput(
                ref format,
                callback,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                0,
                out _queue
            );

            if (status != 0 || _queue == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"AudioQueueNewInput failed: {status}"
                );
            }

            for (int i = 0; i < 3; i++)
            {
                status = AudioQueueAllocateBuffer(
                    _queue,
                    3200,
                    out IntPtr buffer
                );

                if (status == 0)
                {
                    AudioQueueEnqueueBuffer(
                        _queue,
                        buffer,
                        0,
                        IntPtr.Zero
                    );
                }
            }

            status = AudioQueueStart(
                _queue,
                IntPtr.Zero
            );

            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"AudioQueueStart failed: {status}"
                );
            }

            _running = true;
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            if (_queue != IntPtr.Zero)
            {
                AudioQueueStop(_queue, true);
                AudioQueueDispose(_queue, true);
                _queue = IntPtr.Zero;
            }

            if (_callbackHandle.IsAllocated)
            {
                _callbackHandle.Free();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnInput(
            IntPtr userData,
            IntPtr queue,
            IntPtr buffer,
            IntPtr startTime,
            uint numPackets,
            IntPtr packetDesc
        )
        {
            if (buffer == IntPtr.Zero)
            {
                return;
            }

            AudioQueueBuffer data =
                Marshal.PtrToStructure<AudioQueueBuffer>(
                    buffer
                );

            if (data.mAudioDataByteSize > 0 && data.mAudioData != IntPtr.Zero)
            {
                byte[] pcm = new byte[data.mAudioDataByteSize];
                Marshal.Copy(
                    data.mAudioData,
                    pcm,
                    0,
                    (int)data.mAudioDataByteSize
                );
                AudioAvailable?.Invoke(pcm);
            }

            if (_running && queue != IntPtr.Zero)
            {
                AudioQueueEnqueueBuffer(
                    queue,
                    buffer,
                    0,
                    IntPtr.Zero
                );
            }
        }

        private delegate void AudioQueueInputCallback(
            IntPtr userData,
            IntPtr queue,
            IntPtr buffer,
            IntPtr startTime,
            uint numPackets,
            IntPtr packetDesc
        );

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioStreamBasicDescription
        {
            public double mSampleRate;
            public uint mFormatID;
            public uint mFormatFlags;
            public uint mBytesPerPacket;
            public uint mFramesPerPacket;
            public uint mBytesPerFrame;
            public uint mChannelsPerFrame;
            public uint mBitsPerSample;
            public uint mReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioQueueBuffer
        {
            public uint mAudioDataBytesCapacity;
            public IntPtr mAudioData;
            public uint mAudioDataByteSize;
            public IntPtr mUserData;
            public uint mPacketDescriptionCapacity;
            public IntPtr mPacketDescriptions;
            public uint mPacketDescriptionCount;
        }

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueNewInput(
            ref AudioStreamBasicDescription inFormat,
            AudioQueueInputCallback inCallback,
            IntPtr inUserData,
            IntPtr inCallbackRunLoop,
            IntPtr inCallbackRunLoopMode,
            uint inFlags,
            out IntPtr outAQ
        );

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueAllocateBuffer(
            IntPtr inAQ,
            uint inBufferByteSize,
            out IntPtr outBuffer
        );

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueEnqueueBuffer(
            IntPtr inAQ,
            IntPtr inBuffer,
            uint inNumPacketDescs,
            IntPtr inPacketDescs
        );

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueStart(
            IntPtr inAQ,
            IntPtr inStartTime
        );

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueStop(
            IntPtr inAQ,
            bool inImmediate
        );

        [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        private static extern int AudioQueueDispose(
            IntPtr inAQ,
            bool inImmediate
        );
    }
}
