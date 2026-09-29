using System;
using Xunit;
using ZstdSharp.Unsafe;

namespace ZstdSharp.Test
{
    public unsafe class ZbicTest
    {
        private static byte[] RawFrame(params byte[] payload)
        {
            if (payload.Length > 31)
                throw new ArgumentOutOfRangeException(nameof(payload));

            uint blockHeader = ((uint)payload.Length << 3) | 1U;
            byte[] frame = new byte[9 + payload.Length];
            frame[0] = 0x5A;
            frame[1] = 0x42;
            frame[2] = 0x49;
            frame[3] = 0x43;
            frame[4] = 0x20; // single-segment frame, no checksum or dictionary
            frame[5] = (byte)payload.Length;
            frame[6] = (byte)blockHeader;
            frame[7] = (byte)(blockHeader >> 8);
            frame[8] = (byte)(blockHeader >> 16);
            payload.CopyTo(frame, 9);
            return frame;
        }

        private static byte[] StandardRawFrame(params byte[] payload)
        {
            byte[] frame = RawFrame(payload);
            frame[0] = 0x28;
            frame[1] = 0xB5;
            frame[2] = 0x2F;
            frame[3] = 0xFD;
            return frame;
        }

        [Fact]
        public void RawZbicFrameDecompresses()
        {
            byte[] frame = RawFrame((byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o');

            Assert.Equal(5UL, ZbicDecompressor.GetDecompressedSize(frame));
            using var decompressor = new ZbicDecompressor();
            Assert.Equal(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' },
                decompressor.Unwrap(frame).ToArray());
        }

        [Fact]
        public void MultipleZbicFramesDecompress()
        {
            byte[] first = RawFrame((byte)'h', (byte)'i');
            byte[] second = RawFrame((byte)'!');
            byte[] frames = new byte[first.Length + second.Length];
            first.CopyTo(frames, 0);
            second.CopyTo(frames, first.Length);

            Assert.Equal(3UL, ZbicDecompressor.GetDecompressedSize(frames));
            using var decompressor = new ZbicDecompressor();
            Assert.Equal(new byte[] { (byte)'h', (byte)'i', (byte)'!' }, decompressor.Unwrap(frames).ToArray());
        }

        [Fact]
        public void StandardDecompressorRejectsZbicMagic()
        {
            byte[] frame = RawFrame((byte)'x');
            using var decompressor = new Decompressor();
            Assert.Throws<ZstdException>(() => decompressor.Unwrap(frame).ToArray());
        }

        [Fact]
        public void BicNormalizedCountsDecode()
        {
            // Independently derived from the ruzstd-zbic binary-interpolative reader.
            byte[] encoded = { 0x02, 0x60, 0x66, 0x00 };
            short[] counts = new short[53];
            uint maxSymbol = 52;
            uint tableLog = 0;

            fixed (byte* src = encoded)
            fixed (short* dst = counts)
            {
                nuint read = Methods.ZBIC_readNCount(dst, &maxSymbol, &tableLog, src, (nuint)encoded.Length);
                Assert.Equal((nuint)3, read);
            }

            Assert.Equal(5U, tableLog);
            Assert.Equal(1U, maxSymbol);
            Assert.Equal((short)1, counts[0]);
            Assert.Equal((short)31, counts[1]);
        }

        [Fact]
        public void BicLowProbabilityCountsDecode()
        {
            // Header bit 7 enables the low-probability representation, producing -1 states.
            byte[] encoded = { 0x82, 0x0A, 0xC5, 0x00 };
            short[] counts = new short[53];
            uint maxSymbol = 52;
            uint tableLog = 0;

            fixed (byte* src = encoded)
            fixed (short* dst = counts)
            {
                nuint read = Methods.ZBIC_readNCount(dst, &maxSymbol, &tableLog, src, (nuint)encoded.Length);
                Assert.Equal((nuint)3, read);
            }

            Assert.Equal(7U, tableLog);
            Assert.Equal(3U, maxSymbol);
            Assert.Equal(new short[] { -1, -1, -1, 125 }, new[] { counts[0], counts[1], counts[2], counts[3] });
        }

        [Fact]
        public void TruncatedBicTableIsRejected()
        {
            byte[] encoded = { 0x02, 0x60, 0x66 };
            short[] counts = new short[53];
            uint maxSymbol = 52;
            uint tableLog = 0;

            fixed (byte* src = encoded)
            fixed (short* dst = counts)
            {
                nuint result = Methods.ZBIC_readNCount(dst, &maxSymbol, &tableLog, src, (nuint)encoded.Length);
                Assert.NotEqual(0U, Methods.ZSTD_isError(result));
            }
        }

        [Fact]
        public void FailedZbicDecodeDoesNotPoisonStandardDecoder()
        {
            // A one-byte compressed block cannot contain a valid literals/sequence payload.
            byte[] bad =
            {
                0x5A, 0x42, 0x49, 0x43,
                0x20, 0x01,
                0x0D, 0x00, 0x00,
                0x00
            };

            using (var zbic = new ZbicDecompressor())
            {
                Assert.Throws<ZstdException>(() => zbic.Unwrap(bad, new byte[1]));
            }

            byte[] standard = StandardRawFrame((byte)'o', (byte)'k');
            using var zstd = new Decompressor();
            Assert.Equal(new byte[] { (byte)'o', (byte)'k' }, zstd.Unwrap(standard).ToArray());
        }
    }
}
