using System;
using System.IO;
using System.Globalization;

namespace ARNav.Runtime.Import
{
    /// <summary>Limits chosen to keep decoded floor plans practical on mid-range phones.</summary>
    [Serializable]
    public sealed class ImageImportLimits
    {
        public long maximumFileBytes = 32L * 1024L * 1024L;
        public int maximumDimension = 8192;
        public long maximumPixels = 8L * 1024L * 1024L;

        public static ImageImportLimits Default { get { return new ImageImportLimits(); } }

        public void ValidateConfiguration()
        {
            if (maximumFileBytes < 1 || maximumFileBytes > int.MaxValue || maximumDimension < 1 || maximumPixels < 1)
                throw new ArgumentOutOfRangeException("Image import limits must be positive and the file limit must fit in a managed byte array.");
        }
    }

    public sealed class ImageHeaderInfo
    {
        public string format;
        public int width;
        public int height;
    }

    /// <summary>Pure byte-level format and dimension checks performed before texture decode.</summary>
    public static class ImageImportValidation
    {
        public static ImageHeaderInfo Validate(byte[] bytes, ImageImportLimits limits)
        {
            if (limits == null) limits = ImageImportLimits.Default;
            limits.ValidateConfiguration();
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (bytes.LongLength > limits.maximumFileBytes)
                throw new InvalidDataException("This image file exceeds the supported " + (limits.maximumFileBytes / (1024f * 1024f)).ToString("0.#", CultureInfo.InvariantCulture) + " MB limit. Choose a smaller floor-plan image.");

            ImageHeaderInfo header;
            if (IsPng(bytes)) header = ReadPngHeader(bytes);
            else if (IsJpeg(bytes)) header = ReadJpegHeader(bytes);
            else throw new NotSupportedException("This file is not a supported PNG or JPEG image. Choose a .png, .jpg, or .jpeg floor plan.");

            if (header.width <= 0 || header.height <= 0)
                throw new InvalidDataException("The image header has invalid dimensions. Choose a valid PNG or JPEG image.");
            if (header.width > limits.maximumDimension || header.height > limits.maximumDimension)
                throw new InvalidDataException("This image exceeds the supported dimension limit of " + limits.maximumDimension + " pixels on a side. Resize it and try again.");
            if ((long)header.width * header.height > limits.maximumPixels)
                throw new InvalidDataException("This image has too many pixels to import safely (maximum " + (limits.maximumPixels / 1000000f).ToString("0.#", CultureInfo.InvariantCulture) + " megapixels). Resize it and try again.");
            return header;
        }

        public static string DetectFormat(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (IsPng(bytes)) return "png";
            if (IsJpeg(bytes)) return "jpg";
            throw new NotSupportedException("This file is not a supported PNG or JPEG image. Choose a .png, .jpg, or .jpeg floor plan.");
        }

        private static bool IsPng(byte[] bytes)
        {
            return bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47 &&
                   bytes[4] == 0x0d && bytes[5] == 0x0a && bytes[6] == 0x1a && bytes[7] == 0x0a;
        }

        private static bool IsJpeg(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        }

        private static ImageHeaderInfo ReadPngHeader(byte[] bytes)
        {
            if (bytes.Length < 24 || ReadBigEndian32(bytes, 8) != 13 || bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
                throw new InvalidDataException("The PNG header is incomplete or invalid. Choose a valid PNG image.");
            var width = ReadBigEndian32(bytes, 16);
            var height = ReadBigEndian32(bytes, 20);
            if (width > int.MaxValue || height > int.MaxValue) throw new InvalidDataException("The PNG dimensions are invalid or too large.");
            return new ImageHeaderInfo { format = "png", width = (int)width, height = (int)height };
        }

        private static ImageHeaderInfo ReadJpegHeader(byte[] bytes)
        {
            var offset = 2;
            while (offset < bytes.Length)
            {
                if (bytes[offset++] != 0xff) throw new InvalidDataException("The JPEG header is invalid. Choose a valid JPEG image.");
                while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
                if (offset >= bytes.Length) break;
                var marker = bytes[offset++];
                if (marker == 0xd9 || marker == 0xda) break; // end or scan before dimensions is invalid for this import
                if (marker == 0x01 || (marker >= 0xd0 && marker <= 0xd7)) continue;
                if (offset + 2 > bytes.Length) break;
                var segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
                if (segmentLength < 2 || offset + segmentLength > bytes.Length) break;
                if (IsStartOfFrame(marker))
                {
                    if (segmentLength < 7) break;
                    var height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                    var width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                    return new ImageHeaderInfo { format = "jpg", width = width, height = height };
                }
                offset += segmentLength;
            }
            throw new InvalidDataException("The JPEG header is incomplete or has no image dimensions. Choose a valid JPEG image.");
        }

        private static bool IsStartOfFrame(byte marker)
        {
            return (marker >= 0xc0 && marker <= 0xc3) || (marker >= 0xc5 && marker <= 0xc7) ||
                   (marker >= 0xc9 && marker <= 0xcb) || (marker >= 0xcd && marker <= 0xcf);
        }

        private static uint ReadBigEndian32(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }
}
