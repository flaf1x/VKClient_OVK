using System;
using System.Windows;

namespace VKClient.Common.Library
{
    public static class ChatWallpaperEditing
    {
        public static Rect Crop(int width, int height, double viewWidth, double viewHeight, double zoom, double x, double y)
        {
            if (width <= 0 || height <= 0 || viewWidth <= 0 || viewHeight <= 0 || double.IsNaN(zoom)) throw new ArgumentException("Invalid crop bounds");
            double scale = Math.Max(viewWidth / width, viewHeight / height) * Math.Max(1, Math.Min(3, zoom));
            x = ClampOffset(x, width * scale - viewWidth); y = ClampOffset(y, height * scale - viewHeight);
            int left = Math.Max(0, (int)Math.Round((width * scale - viewWidth) / 2 / scale - x / scale));
            int top = Math.Max(0, (int)Math.Round((height * scale - viewHeight) / 2 / scale - y / scale));
            int w = Math.Max(1, Math.Min(width - left, (int)Math.Round(viewWidth / scale)));
            int h = Math.Max(1, Math.Min(height - top, (int)Math.Round(viewHeight / scale)));
            return new Rect(left, top, w, h);
        }

        public static double ClampOffset(double value, double overflow)
        {
            double limit = Math.Max(0, overflow / 2);
            return double.IsNaN(value) ? 0 : Math.Max(-limit, Math.Min(limit, value));
        }

        // Linear-time separable blur, done once per selected image, not per
        // preview frame or per chat message. No full-resolution camera bitmap.
        public static int[] Blur(int[] pixels, int width, int height, int radius)
        {
            if (width <= 0 || height <= 0 || pixels == null || pixels.Length != width * height) throw new ArgumentException("Invalid image pixels");
            radius = Math.Max(1, Math.Min(16, radius));
            var horizontal = new int[pixels.Length]; var output = new int[pixels.Length];
            BlurPass(pixels, horizontal, width, height, radius, false);
            BlurPass(horizontal, output, width, height, radius, true);
            return output;
        }

        private static void BlurPass(int[] input, int[] output, int width, int height, int radius, bool vertical)
        {
            int lines = vertical ? width : height, length = vertical ? height : width, count = radius * 2 + 1;
            for (int line = 0; line < lines; line++)
            {
                int a = 0, r = 0, g = 0, b = 0;
                for (int k = -radius; k <= radius; k++) Add(input[Index(line, Math.Max(0, Math.Min(length - 1, k)), width, vertical)], 1, ref a, ref r, ref g, ref b);
                for (int p = 0; p < length; p++)
                {
                    output[Index(line, p, width, vertical)] = ((a / count) << 24) | ((r / count) << 16) | ((g / count) << 8) | (b / count);
                    Add(input[Index(line, Math.Max(0, p - radius), width, vertical)], -1, ref a, ref r, ref g, ref b);
                    Add(input[Index(line, Math.Min(length - 1, p + radius + 1), width, vertical)], 1, ref a, ref r, ref g, ref b);
                }
            }
        }
        private static int Index(int line, int p, int width, bool vertical) { return vertical ? p * width + line : line * width + p; }
        private static void Add(int color, int sign, ref int a, ref int r, ref int g, ref int b)
        { a += sign * (int)((uint)color >> 24); r += sign * ((color >> 16) & 255); g += sign * ((color >> 8) & 255); b += sign * (color & 255); }
    }
}
