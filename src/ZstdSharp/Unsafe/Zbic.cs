namespace ZstdSharp.Unsafe
{
    public static unsafe partial class Methods
    {
        public const uint ZBIC_MAGICNUMBER = 0x4349425A;

        [System.ThreadStatic]
        private static bool _zbicNCountMode;

        internal static bool ZBIC_setNCountMode(bool enabled)
        {
            bool previous = _zbicNCountMode;
            _zbicNCountMode = enabled;
            return previous;
        }

        internal static bool ZBIC_isNCountMode()
        {
            return _zbicNCountMode;
        }

        /// <summary>
        /// Rewrites each ZBIC frame magic to the normal Zstandard magic in a private mutable input copy.
        /// This lets the unmodified Zstandard frame/block walker handle framing while ZBIC mode changes
        /// only the normalized FSE probability-table representation.
        /// </summary>
        internal static nuint ZBIC_normalizeFrameMagics(byte* src, nuint srcSize)
        {
            byte* ip = src;
            nuint remaining = srcSize;

            while (remaining != 0)
            {
                if (remaining < 4)
                    return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_srcSize_wrong));

                uint magic = MEM_readLE32(ip);
                if ((magic & 0xFFFFFFF0) == 0x184D2A50)
                {
                    nuint skippableSize = ZSTD_findFrameCompressedSize(ip, remaining);
                    if (ERR_isError(skippableSize))
                        return skippableSize;
                    if (skippableSize == 0 || skippableSize > remaining)
                        return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_srcSize_wrong));

                    ip += skippableSize;
                    remaining -= skippableSize;
                    continue;
                }

                if (magic != ZBIC_MAGICNUMBER)
                    return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_prefix_unknown));

                MEM_writeLE32(ip, 0xFD2FB528);
                nuint frameSize = ZSTD_findFrameCompressedSize(ip, remaining);
                if (ERR_isError(frameSize))
                    return frameSize;
                if (frameSize == 0 || frameSize > remaining)
                    return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_srcSize_wrong));

                ip += frameSize;
                remaining -= frameSize;
            }

            return 0;
        }
    }
}
