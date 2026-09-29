using System;
using ZstdSharp.Unsafe;

namespace ZstdSharp
{
    /// <summary>
    /// Decompresses Nintendo ZBIC frames. ZBIC keeps the Zstandard frame and block layout,
    /// but uses a different frame magic and binary-interpolative FSE normalized-count tables.
    /// </summary>
    public unsafe sealed class ZbicDecompressor : IDisposable
    {
        private readonly SafeDctxHandle handle;

        public ZbicDecompressor()
        {
            handle = SafeDctxHandle.Create();
        }

        private static byte[] NormalizeFrames(ReadOnlySpan<byte> src)
        {
            byte[] normalized = src.ToArray();
            fixed (byte* srcPtr = normalized)
            {
                Methods.ZBIC_normalizeFrameMagics(srcPtr, (nuint)normalized.Length).EnsureZstdSuccess();
            }

            return normalized;
        }

        public static ulong GetDecompressedSize(ReadOnlySpan<byte> src)
        {
            byte[] normalized = NormalizeFrames(src);
            fixed (byte* srcPtr = normalized)
            {
                return Methods.ZSTD_decompressBound(srcPtr, (nuint)normalized.Length).EnsureContentSizeOk();
            }
        }

        public static ulong GetDecompressedSize(ArraySegment<byte> src)
            => GetDecompressedSize((ReadOnlySpan<byte>)src);

        public static ulong GetDecompressedSize(byte[] src, int srcOffset, int srcLength)
            => GetDecompressedSize(new ReadOnlySpan<byte>(src, srcOffset, srcLength));

        public Span<byte> Unwrap(ReadOnlySpan<byte> src, int maxDecompressedSize = int.MaxValue)
        {
            ulong expectedDstSize = GetDecompressedSize(src);
            if (expectedDstSize > (ulong)maxDecompressedSize)
                throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall,
                    $"Decompressed content size {expectedDstSize} is greater than {nameof(maxDecompressedSize)} {maxDecompressedSize}");
            if (expectedDstSize > Constants.MaxByteArrayLength)
                throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall,
                    $"Decompressed content size {expectedDstSize} is greater than max possible byte array size {Constants.MaxByteArrayLength}");

            byte[] dest = new byte[expectedDstSize];
            int length = Unwrap(src, dest);
            return new Span<byte>(dest, 0, length);
        }

        public int Unwrap(byte[] src, byte[] dest, int offset)
            => Unwrap(src, new Span<byte>(dest, offset, dest.Length - offset));

        public int Unwrap(ReadOnlySpan<byte> src, Span<byte> dest)
        {
            byte[] normalized = NormalizeFrames(src);
            fixed (byte* srcPtr = normalized)
            fixed (byte* destPtr = dest)
            {
                bool previousMode = Methods.ZBIC_setNCountMode(true);
                try
                {
                    using var dctx = handle.Acquire();
                    return (int)Methods
                        .ZSTD_decompressDCtx(dctx, destPtr, (nuint)dest.Length, srcPtr, (nuint)normalized.Length)
                        .EnsureZstdSuccess();
                }
                finally
                {
                    Methods.ZBIC_setNCountMode(previousMode);
                }
            }
        }

        public int Unwrap(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
            => Unwrap(new ReadOnlySpan<byte>(src, srcOffset, srcLength), new Span<byte>(dst, dstOffset, dstLength));

        public void Dispose()
        {
            handle.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
