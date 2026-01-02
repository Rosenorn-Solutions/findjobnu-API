using SkiaSharp;

namespace FindjobnuService.Utilities
{
    public static class WebPConverter
    {
        private const int DefaultQuality = 80;

        public static (byte[] bytes, string? format, string? mimeType) ConvertToWebP(byte[]? input, int quality = DefaultQuality)
        {
            if (input == null || input.Length == 0)
                return (input ?? Array.Empty<byte>(), null, null);

            quality = Math.Clamp(quality, 1, 100);

            if (IsWebP(input))
            {
                return (input, "webp", "image/webp");
            }

            try
            {
                using var bitmap = SKBitmap.Decode(input);
                if (bitmap == null)
                {
                    var originalMime = DetectMimeType(input);
                    return (input, "original", originalMime);
                }

                using var image = SKImage.FromBitmap(bitmap);
                if (image == null)
                {
                    var originalMime = DetectMimeType(input);
                    return (input, "original", originalMime);
                }

                using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
                if (data == null)
                {
                    var originalMime = DetectMimeType(input);
                    return (input, "original", originalMime);
                }

                return (data.ToArray(), "webp", "image/webp");
            }
            catch
            {
                var originalMime = DetectMimeType(input);
                return (input, "original", originalMime);
            }
        }

        private static bool IsWebP(byte[] data)
        {
            return data.Length >= 12 &&
                   data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                   data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P';
        }

        private static string DetectMimeType(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return "image/jpeg";
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
                return "image/png";
            if (IsWebP(data))
                return "image/webp";
            return "application/octet-stream";
        }
    }
}
