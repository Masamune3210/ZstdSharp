using System;
using Xunit;
using ZstdSharp.Unsafe;

namespace ZstdSharp.Test
{
    public unsafe class ZbicTest
    {
        private static byte[] Hex(string value)
        {
            if ((value.Length & 1) != 0)
                throw new ArgumentException("Hex input must have an even length.", nameof(value));

            byte[] result = new byte[value.Length / 2];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            }

            return result;
        }

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
        public void AtmosphereCompressedEntropyVectorDecompresses()
        {
            // Produced from synthetic bytes by Atmosphere's ZBIC-enabled Zstandard compressor.
            // Unlike RawZbicFrameDecompresses, this contains compressed entropy tables and therefore
            // exercises the binary-interpolative FSE probability-table path.
            const string encodedHex = "5a424943600001450b0094120000899aabbccddef0061728394a5b6d7e8fa0b1c2d3e5f60c1d2e3f5062738495a6b7c8daeb01122334455768798a9bacbdcfe0f10718293a4c5d6e7f90a1b2c4d5e6f70d1e2f415263748596a7b9cadbec021324364758697a8b9caebfd0e1f208192b3c4d5e6f8091a3b4c5d6e7f80e2031425364758698a9bacbdced0315263748596a7b8d9eafc0d1e2f30a1b2c3d4e5f708293a4b5c6d7e8fa102132435465778899aabbccddef05162738495a6c7d8e9fb0c1d2e4f50b1c2d3e4f61728394a5b6c7d9ea0011223344566778cedf4b5cc3d44051b8c93546adbe2a3ba2b31f3097a814258c9d091a8192f90f7687ee046b7ce3f46071d8e95566cdde4a5bc2d33f50b7c83445acbd293aa1b21e2f96a713248b9c08198091f80e7586ed036a7b8c9daebfd0e22b200466ae2b6b820f372cf870c3820f372cf870c3820f372cf870c3821d6e58f0e186051f6e58f0e186051f6e58f0e186051f6e986070b809b5725858";
            byte[] encoded = Hex(encodedHex);
            byte[] expected = new byte[512];
            for (int index = 0; index < expected.Length; index++)
            {
                expected[index] = (byte)((index * 17 + index / 7) % 251);
            }
            Array.Clear(expected, 0, 8);

            Assert.Equal(512UL, ZbicDecompressor.GetDecompressedSize(encoded));
            using var decompressor = new ZbicDecompressor();
            Assert.Equal(expected, decompressor.Unwrap(encoded).ToArray());
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
                Assert.True(Methods.ZSTD_isError(result));
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
