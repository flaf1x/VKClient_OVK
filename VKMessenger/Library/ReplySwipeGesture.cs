using System;
namespace VKMessenger.Library
{
    public sealed class ReplySwipeGesture
    {
        private int _direction;
        private double _x;
        public bool IsHorizontal { get { return _direction == 1; } }
        public void Reset() { _direction = 0; _x = 0; }
        public double Update(double x, double y)
        {
            if (_direction == 0 && Math.Max(Math.Abs(x), Math.Abs(y)) >= 12)
                _direction = x < 0 && -x > Math.Abs(y) * 1.6 ? 1 : -1;
            if (_direction != 1) return 0;
            _x = Math.Abs(y) > Math.Max(32, Math.Abs(x)) ? 0 : x;
            return Math.Max(-76, Math.Min(0, _x * 0.65));
        }
        public bool Complete() { bool reply = IsHorizontal && _x <= -64; Reset(); return reply; }

        public static void MapVector(double x, double y, double xx, double xy, double yx, double yy, out double mappedX, out double mappedY)
        {
            mappedX = xx * x + yx * y;
            mappedY = xy * x + yy * y;
        }

        public static bool UnmapVector(double x, double y, double xx, double xy, double yx, double yy, out double localX, out double localY)
        {
            double determinant = xx * yy - xy * yx;
            localX = localY = 0;
            if (Math.Abs(determinant) < 0.000001) return false;
            localX = (yy * x - yx * y) / determinant;
            localY = (-xy * x + xx * y) / determinant;
            return true;
        }
    }
}
