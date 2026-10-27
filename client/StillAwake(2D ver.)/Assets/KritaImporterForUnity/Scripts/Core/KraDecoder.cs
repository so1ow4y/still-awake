using System;
using Unity.Mathematics;
using UnityEngine;
using LZF;

namespace KraImporter.Core
{
    /// <summary>
    /// KraDecoder handles low-level conversion of Krita tile payloads into Unity pixel data. The class is internal and is used by KraParser.
    /// </summary>
    class KraDecoder
    {
        enum KraFlag
        {
            RAW_DATA_FLAG = 0,
            COMPRESSED_DATA_FLAG = 1
        }

        /// <summary>
        /// Decodes one Krita tile payload. The first byte is used as a data flag. RAW data is copied directly, while compressed data is decompressed through LZF and then delinearized. <br />
        /// Supported flags: <see cref="KraFlag"/>
        /// </summary>
        internal static byte[] DecodeKritaTileV2(byte[] tilePayload, int pixelSize, int tileWidth, int tileHeight)
        {
            int pixelCount = tileWidth * tileHeight;
            int tileDataSize = pixelCount * pixelSize;

            if (tilePayload == null || tilePayload.Length == 0)
                throw new Exception("Empty Krita tile payload.");

            byte flag = tilePayload[0];

            if (flag == (byte)KraFlag.RAW_DATA_FLAG)
            {
                if (tilePayload.Length - 1 < tileDataSize)
                {
                    throw new Exception(
                        $"RAW tile payload too small. Got {tilePayload.Length - 1}, expected {tileDataSize}"
                    );
                }

                byte[] raw = new byte[tileDataSize];
                Buffer.BlockCopy(tilePayload, 1, raw, 0, tileDataSize);
                return raw;
            }

            if (flag == (byte)KraFlag.COMPRESSED_DATA_FLAG)
            {
                int lzfSize = tilePayload.Length - 1;

                byte[] lzfInput = new byte[lzfSize];
                Buffer.BlockCopy(tilePayload, 1, lzfInput, 0, lzfSize);

                byte[] linearized = CLZF2.Decompress(lzfInput, lzfSize);

                if (linearized.Length != tileDataSize)
                {
                    throw new Exception(
                        $"LZF output size mismatch. Got {linearized.Length}, expected {tileDataSize}"
                    );
                }

                return DelinearizeColors(linearized, pixelSize);
            }

            throw new Exception($"Unknown Krita tile payload flag: {flag}");
        }

        /// <summary>
        /// Converts Krita channel-linearized color data into pixel-interleaved data. This changes data from channel blocks into per-pixel layout.
        /// </summary>
        internal static byte[] DelinearizeColors(byte[] linearized, int pixelSize)
        {
            const int tileWidth = 64;
            const int tileHeight = 64;

            int pixelCount = tileWidth * tileHeight;
            int tileDataSize = pixelCount * pixelSize;

            if (linearized.Length != tileDataSize)
            {
                throw new Exception(
                    $"Invalid linearized data size. Got {linearized.Length}, expected {tileDataSize}"
                );
            }

            byte[] result = new byte[tileDataSize];

            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                for (int channel = 0; channel < pixelSize; channel++)
                {
                    result[pixel * pixelSize + channel] =
                        linearized[channel * pixelCount + pixel];
                }
            }

            return result;
        }

        static Color32 KritaRawPixelToUnityColor(byte[] data, int p, byte opacity)
        {
            byte c0 = data[p + 0];
            byte c1 = data[p + 1];
            byte c2 = data[p + 2];
            byte c3 = data[p + 3];

            byte finalAlpha = (byte)((c3 * opacity) / 255);

            // BGRA -> RGBA
            return new Color32(
                c2, // B -> R
                c1, // G
                c0, // R -> B
                finalAlpha  // A
            );
        }

        /// <summary>
        /// Converts decoded raw tile bytes into Unity Color32[]. It also converts BGRA channel order to RGBA and applies Krita layer opacity to pixel alpha.
        /// </summary>
        internal static Color32[] RawRgbaToPixels(byte[] rawTileData, int tileWidth, int tileHeight, byte opacity)
        {
            int pixelCount = tileWidth * tileHeight;

            Color32[] pixels = new Color32[pixelCount];

            for (int i = 0; i < pixelCount; i++)
            {
                int p = i * 4;

                pixels[i] = KritaRawPixelToUnityColor(rawTileData, p, opacity);
            }

            return pixels;
        } 

        static Color32 KritaRawPixelToUnityColorOneChannel(byte[] data, int p, byte opacity)
        {
            byte c0 = data[p];

            byte finalAlpha = (byte)((c0 * opacity) / 255);

            return new Color32(c0, c0, c0, finalAlpha);
        }

        internal static Color32[] RawRgbaToPixelsOneChannel(byte[] rawTileData, int tileWidth, int tileHeight, byte opacity)
        {
            int pixelCount = tileWidth * tileHeight;

            Color32[] pixels = new Color32[pixelCount];

            for (int i = 0; i < pixelCount; i++)
                pixels[i] = KritaRawPixelToUnityColorOneChannel(rawTileData, i, opacity);

            return pixels;
        }
    }

}
