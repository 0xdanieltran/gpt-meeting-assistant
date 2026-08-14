using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;

namespace PrivateBrowser
{
    public static class AudioConverter
    {
        public static byte[] To16KhzMonoPcm16(
            byte[] source,
            WaveFormat sourceFormat
        )
        {
            using MemoryStream inputStream =
                new MemoryStream(source);

            using RawSourceWaveStream rawStream =
                new RawSourceWaveStream(
                    inputStream,
                    sourceFormat
                );

            ISampleProvider sampleProvider =
                rawStream.ToSampleProvider();

            if (
                sampleProvider
                    .WaveFormat
                    .Channels == 2
            )
            {
                sampleProvider =
                    new StereoToMonoSampleProvider(
                        sampleProvider
                    )
                    {
                        LeftVolume = 0.5f,
                        RightVolume = 0.5f
                    };
            }

            WdlResamplingSampleProvider resampler =
                new WdlResamplingSampleProvider(
                    sampleProvider,
                    16000
                );

            IWaveProvider pcmProvider =
                new SampleToWaveProvider16(
                    resampler
                );

            using MemoryStream output =
                new MemoryStream();

            byte[] buffer =
                new byte[4096];

            while (true)
            {
                int read =
                    pcmProvider.Read(
                        buffer,
                        0,
                        buffer.Length
                    );

                if (read <= 0)
                {
                    break;
                }

                output.Write(
                    buffer,
                    0,
                    read
                );
            }

            return output.ToArray();
        }
    }
}