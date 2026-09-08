using System.Buffers.Binary;

namespace PrivateBrowser.Mac
{
    public static class WavWriter
    {
        public static MemoryStream CreatePcm16Mono16kStream(
            byte[] pcm16Audio
        )
        {
            MemoryStream stream =
                new MemoryStream();

            const int sampleRate = 16000;
            const short channels = 1;
            const short bitsPerSample = 16;
            int byteRate =
                sampleRate * channels * bitsPerSample / 8;
            short blockAlign =
                (short)(channels * bitsPerSample / 8);

            stream.Write("RIFF"u8);
            WriteInt32(stream, 36 + pcm16Audio.Length);
            stream.Write("WAVE"u8);
            stream.Write("fmt "u8);
            WriteInt32(stream, 16);
            WriteInt16(stream, 1);
            WriteInt16(stream, channels);
            WriteInt32(stream, sampleRate);
            WriteInt32(stream, byteRate);
            WriteInt16(stream, blockAlign);
            WriteInt16(stream, bitsPerSample);
            stream.Write("data"u8);
            WriteInt32(stream, pcm16Audio.Length);
            stream.Write(pcm16Audio);
            stream.Position = 0;
            return stream;
        }

        private static void WriteInt32(
            Stream stream,
            int value
        )
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(
                buffer,
                value
            );
            stream.Write(buffer);
        }

        private static void WriteInt16(
            Stream stream,
            short value
        )
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(
                buffer,
                value
            );
            stream.Write(buffer);
        }
    }
}
