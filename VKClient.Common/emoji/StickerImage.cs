using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using System.IO.IsolatedStorage;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using VKClient.Common.Framework;
using VKClient.Common.Utils;

namespace VKClient.Common.Emoji
{
    // A small native Lottie shape renderer. No WebView per message and no
    // third-party service receives sticker URLs or account credentials.
    // Supports vector paths, groups, fills/strokes, parenting and keyframes.
    public class StickerImage : UserControl
    {
        public static readonly DependencyProperty SourceUriProperty = DependencyProperty.Register(
            "SourceUri", typeof(string), typeof(StickerImage), new PropertyMetadata(null, SourceChanged));
        public string SourceUri { get { return (string)GetValue(SourceUriProperty); } set { SetValue(SourceUriProperty, value); } }
        public bool Animate { get; set; }
        private readonly Image _image = new Image();
        public Stretch Stretch { get { return _image.Stretch; } set { _image.Stretch = value; } }
        private ImageSource Source { get { return _image.Source; } set { _image.Source = value; } }
        private static readonly Dictionary<string, JObject> Cache = new Dictionary<string, JObject>();
        private static readonly Dictionary<string, ImageSource> PreviewCache = new Dictionary<string, ImageSource>();
        private sealed class FrameSequence
        {
            public JObject Animation;
            public ImageSource[] Frames;
            public int Prepared, Pixels;
            public double Duration, FirstFrame, LastFrame;
            public DateTime Started, LastUsed;
            public bool Failed;
            public long Bytes { get { return (long)Frames.Length * Pixels * Pixels * 4; } }
        }
        private static readonly Dictionary<string, FrameSequence> FrameCache = new Dictionary<string, FrameSequence>();
        private static readonly List<WeakReference> Players = new List<WeakReference>();
        private static readonly DispatcherTimer PlaybackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private static readonly DispatcherTimer PreparationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private const long FrameCacheBudget = 16 * 1024 * 1024;
        private static DateTime _deferUntil;
        private FrameSequence _sequence;
        private bool _registered;
        private JObject _animation;
        private WebClient _download;
        private bool _loaded;
        private int _generation;

        static StickerImage()
        {
            PlaybackTimer.Tick += (s, e) => PlaybackTick();
            PreparationTimer.Tick += (s, e) => PrepareNextFrame();
        }

        public StickerImage()
        {
            Content = _image;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Stretch = Stretch.Uniform;
            Loaded += (s, e) => { _loaded = true; LoadSource(); };
            Unloaded += (s, e) => { _loaded = false; Stop(); };
        }

        private static void SourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var image = (StickerImage)d;
            image.Stop(); image._animation = null; image.Source = null;
            if (image._loaded) image.LoadSource();
        }

        private void Stop()
        {
            ++_generation;
            _sequence = null; _registered = false;
            Players.RemoveAll(w => !w.IsAlive || object.ReferenceEquals(w.Target, this));
            if (Players.Count == 0) { PlaybackTimer.Stop(); PreparationTimer.Stop(); }
            if (_download != null) { _download.CancelAsync(); _download = null; }
        }

        private void LoadSource()
        {
            string url = SourceUri;
            if (string.IsNullOrEmpty(url)) return;
            if (!url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (!url.Contains("://") && url.StartsWith("OpenVKSticker_", StringComparison.Ordinal))
                {
                    try
                    {
                        using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                        using (var stream = store.OpenFile(url, FileMode.Open, FileAccess.Read))
                        { var bitmap = new BitmapImage(); bitmap.SetSource(stream); Source = bitmap; }
                    }
                    catch (Exception ex) { Logger.Instance.Info("Cached sticker unavailable: {0}", ex.GetType().Name); }
                }
                else ImageLoader.SetUriSource(_image, url);
                return;
            }
            ImageLoader.SetUriSource(_image, null);
            ImageSource preview;
            if (PreviewCache.TryGetValue(PreviewKey(), out preview)) Source = preview;
            if (_animation != null || Cache.TryGetValue(url, out _animation)) { Start(); return; }
            if (!url.Contains("://"))
            {
                try
                {
                    using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                    using (var stream = store.OpenFile(url, FileMode.Open, FileAccess.Read))
                    using (var reader = new StreamReader(stream)) _animation = JObject.Parse(reader.ReadToEnd());
                    Start();
                }
                catch (Exception ex) { Logger.Instance.Info("Cached sticker unavailable: {0}", ex.GetType().Name); }
                return;
            }
            Uri uri;
            // Only public sticker assets of the selected instance are fetched.
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !OpenVKInstanceManager.IsCurrentInstanceUrl(url) || !uri.AbsolutePath.StartsWith("/sticker/")) return;
            int generation = ++_generation;
            _download = new WebClient();
            _download.DownloadStringCompleted += (s, e) =>
            {
                if (generation != _generation || !_loaded || e.Cancelled) return;
                _download = null;
                try
                {
                    if (e.Error != null) throw e.Error;
                    if (e.Result.Length > 2000000) throw new InvalidOperationException("Sticker animation too large");
                    _animation = JObject.Parse(e.Result);
                    if (!(_animation["layers"] is JArray)) throw new InvalidOperationException("Invalid sticker animation");
                    if (Cache.Count >= 8) Cache.Clear();
                    Cache[url] = _animation;
                    Start();
                }
                catch (Exception ex) { Logger.Instance.Info("Animated sticker unavailable: {0}", ex.GetType().Name); }
            };
            _download.DownloadStringAsync(uri);
        }

        private void Start()
        {
            ImageSource preview;
            if (PreviewCache.TryGetValue(PreviewKey(), out preview)) Source = preview;
            else RenderFrame();
            if (Animate && !_registered && _loaded && _animation != null)
            {
                _registered = true;
                Players.Add(new WeakReference(this));
                PlaybackTimer.Start();
            }
        }

        private int RasterSize() { return (int)Math.Max(64, Math.Min(256, Width > 0 && !double.IsNaN(Width) ? Width * 1.5 : 256)); }
        private string PreviewKey() { return (SourceUri ?? "") + "#" + RasterSize(); }

        // Give gestures priority over one-time vector rasterization. Playback
        // itself only selects an existing bitmap; it never builds a visual tree.
        public static void DeferAnimations() { _deferUntil = DateTime.UtcNow.AddMilliseconds(220); }

        private bool IsOnScreen()
        {
            if (!_loaded || Visibility != Visibility.Visible) return false;
#if DESKTOP_RENDER_TEST
            return true;
#else
            var root = Application.Current.RootVisual as FrameworkElement;
            if (root == null || ActualWidth <= 0 || ActualHeight <= 0) return false;
            try
            {
                var transform = TransformToVisual(root);
                Point a = transform.Transform(new Point(0, 0));
                Point b = transform.Transform(new Point(ActualWidth, ActualHeight));
                return Math.Max(a.X, b.X) > 0 && Math.Min(a.X, b.X) < root.ActualWidth &&
                    Math.Max(a.Y, b.Y) > 0 && Math.Min(a.Y, b.Y) < root.ActualHeight;
            }
            catch (ArgumentException) { return false; }
#endif
        }

        private static List<StickerImage> LivePlayers()
        {
            Players.RemoveAll(w => !w.IsAlive || !(w.Target is StickerImage) || !((StickerImage)w.Target)._registered);
            return Players.Select(w => w.Target as StickerImage).Where(p => p != null).ToList();
        }

        private static void PlaybackTick()
        {
            var players = LivePlayers();
            if (players.Count == 0) { PlaybackTimer.Stop(); PreparationTimer.Stop(); return; }
            if (DateTime.UtcNow < _deferUntil) return;
            foreach (var player in players)
            {
                if (!player.Animate || !player.IsOnScreen() || player._animation == null) continue;
                if (player._sequence == null) player._sequence = player.GetSequence();
                var sequence = player._sequence;
                if (sequence == null || sequence.Failed) continue;
                sequence.LastUsed = DateTime.UtcNow;
                if (sequence.Prepared != sequence.Frames.Length) { PreparationTimer.Start(); continue; }
                double elapsed = (DateTime.UtcNow - sequence.Started).TotalSeconds;
                int frame = (int)((elapsed % sequence.Duration) / sequence.Duration * sequence.Frames.Length);
                player.Source = sequence.Frames[Math.Max(0, Math.Min(sequence.Frames.Length - 1, frame))];
            }
        }

        private FrameSequence GetSequence()
        {
            string key = PreviewKey();
            FrameSequence sequence;
            if (FrameCache.TryGetValue(key, out sequence)) return sequence;
            double first = Number(_animation["ip"]), last = Number(_animation["op"], first + 1);
            double duration = Math.Max(.04, (last - first) / Math.Max(1, Number(_animation["fr"], 30)));
            int count = Math.Max(1, Math.Min(120, (int)Math.Ceiling(duration * Math.Min(30, Number(_animation["fr"], 30)))));
            // Reserve at most 8 MB per sequence, 16 MB for the shared cache.
            int pixels = Math.Min(RasterSize(), (int)Math.Sqrt(8 * 1024 * 1024 / (count * 4.0)));
            sequence = new FrameSequence { Animation = _animation, Frames = new ImageSource[count], Pixels = pixels,
                Duration = duration, FirstFrame = first, LastFrame = Math.Max(first + 1, last), LastUsed = DateTime.UtcNow };
            var players = LivePlayers();
            while (FrameCache.Values.Sum(s => s.Bytes) + sequence.Bytes > FrameCacheBudget)
            {
                var candidates = FrameCache.Where(pair => !players.Any(p => p._sequence == pair.Value && p.IsOnScreen()))
                    .OrderBy(pair => pair.Value.LastUsed).ToList();
                if (candidates.Count == 0) return null;
                var old = candidates[0];
                foreach (var player in players.Where(p => p._sequence == old.Value))
                {
                    player._sequence = null;
                    ImageSource poster;
                    if (PreviewCache.TryGetValue(player.PreviewKey(), out poster)) player.Source = poster;
                }
                FrameCache.Remove(old.Key);
            }
            FrameCache[key] = sequence;
            return sequence;
        }

        private static void PrepareNextFrame()
        {
            if (DateTime.UtcNow < _deferUntil) return;
            var players = LivePlayers();
            var sequence = players.Where(p => p.IsOnScreen()).Select(p => p._sequence)
                .FirstOrDefault(s => s != null && !s.Failed && s.Prepared < s.Frames.Length);
            if (sequence == null) { PreparationTimer.Stop(); return; }
            try
            {
                double frame = sequence.FirstFrame + (sequence.LastFrame - sequence.FirstFrame) * sequence.Prepared / sequence.Frames.Length;
                sequence.Frames[sequence.Prepared++] = Rasterize(sequence.Animation, sequence.Pixels, frame);
                if (sequence.Prepared == sequence.Frames.Length) { sequence.Animation = null; sequence.Started = DateTime.UtcNow; }
            }
            catch (Exception ex)
            {
                sequence.Failed = true;
                sequence.Animation = null;
                sequence.Frames = new ImageSource[0];
                Logger.Instance.Info("Animated sticker preparation failed: {0}", ex.GetType().Name);
            }
        }

        private static double Number(JToken value, double fallback = 0)
        { return value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) ? (double)value : fallback; }

        // Static properties and hold/linear animated properties, including
        // shape morphs. Temporal bezier easing is approximated linearly.
        internal static JToken Value(JToken property, double frame)
        {
            if (property == null) return null;
            JToken k = property is JObject ? property["k"] : property;
            JArray keys = k as JArray;
            if (keys == null || keys.Count == 0 || !(keys[0] is JObject) || keys[0]["t"] == null) return k;
            JObject current = (JObject)keys[0], next = null;
            foreach (JObject key in keys.OfType<JObject>())
            { if (Number(key["t"]) <= frame) current = key; else { next = key; break; } }
            JToken start = current["s"], end = current["e"] ?? (next == null ? null : next["s"]);
            if (next == null || Number(current["h"]) == 1 || end == null) return Unwrap(start);
            double fraction = Math.Max(0, Math.Min(1, (frame - Number(current["t"])) / Math.Max(.001, Number(next["t"]) - Number(current["t"]))));
            return Blend(Unwrap(start), Unwrap(end), Ease(current, fraction));
        }

        private static double Ease(JObject key, double fraction)
        {
            if (key["o"] == null || key["i"] == null) return fraction;
            double x1 = Component(key["o"]["x"], 0, .333), y1 = Component(key["o"]["y"], 0, .333);
            double x2 = Component(key["i"]["x"], 0, .667), y2 = Component(key["i"]["y"], 0, .667);
            double low = 0, high = 1;
            for (int i = 0; i < 14; ++i)
            {
                double t = (low + high) / 2, s = 1 - t;
                double x = 3 * s * s * t * x1 + 3 * s * t * t * x2 + t * t * t;
                if (x < fraction) low = t; else high = t;
            }
            double u = (low + high) / 2, v = 1 - u;
            return 3 * v * v * u * y1 + 3 * v * u * u * y2 + u * u * u;
        }

        private static JToken Unwrap(JToken token)
        { return token is JArray && token.Count() == 1 && token[0] is JObject ? token[0] : token; }
        private static JToken Blend(JToken a, JToken b, double t)
        {
            if (a == null || b == null) return a;
            if (a is JArray && b is JArray && a.Count() == b.Count())
                return new JArray(a.Select((v, i) => Blend(v, b[i], t)));
            if (a is JObject && b is JObject)
            {
                var result = (JObject)a.DeepClone();
                foreach (string name in new[] { "v", "i", "o" }) if (b[name] != null) result[name] = Blend(a[name], b[name], t);
                return result;
            }
            if (a.Type == JTokenType.Float || a.Type == JTokenType.Integer) return new JValue(Number(a) + (Number(b) - Number(a)) * t);
            return a;
        }

        private static double Component(JToken value, int index, double fallback = 0)
        { return value is JArray ? (value.Count() > index ? Number(value[index], fallback) : fallback) : Number(value, fallback); }
        private static Point PointAt(JToken value) { return new Point(Component(value, 0), Component(value, 1)); }
        private static Point Add(Point a, Point b) { return new Point(a.X + b.X, a.Y + b.Y); }

        private static void Transform(Canvas canvas, JToken ks, double frame)
        {
            if (ks == null) return;
            var anchor = PointAt(Value(ks["a"], frame));
            var position = PointAt(Value(ks["p"], frame));
            if (ks["p"] != null && ks["p"]["x"] != null)
                position = new Point(Component(Value(ks["p"]["x"], frame), 0), Component(Value(ks["p"]["y"], frame), 0));
            var scale = Value(ks["s"], frame);
            var group = new TransformGroup();
            group.Children.Add(new TranslateTransform { X = -anchor.X, Y = -anchor.Y });
            group.Children.Add(new ScaleTransform { ScaleX = Component(scale, 0, 100) / 100, ScaleY = Component(scale, 1, 100) / 100 });
            group.Children.Add(new RotateTransform { Angle = Component(Value(ks["r"] ?? ks["rz"], frame), 0) });
            group.Children.Add(new TranslateTransform { X = position.X, Y = position.Y });
            canvas.RenderTransform = group;
            canvas.Opacity = Math.Max(0, Math.Min(1, Component(Value(ks["o"], frame), 0, 100) / 100));
        }

        private static Brush BrushFor(JToken shape, double frame)
        {
            if (shape == null) return null;
            JToken color = Value(shape["c"], frame);
            return new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Max(0, Math.Min(1, Component(Value(shape["o"], frame), 0, 100) / 100))),
                Channel(color, 0), Channel(color, 1), Channel(color, 2)));
        }
        private static byte Channel(JToken c, int index) { return (byte)Math.Max(0, Math.Min(255, Component(c, index) * 255)); }

        private static Geometry PathGeometryFor(JToken shape)
        {
            if (shape == null || !(shape["v"] is JArray)) return null;
            var vertices = (JArray)shape["v"];
            if (vertices.Count == 0 || vertices.Count > 3000) return null;
            bool closed = (bool?)shape["c"] ?? false;
            var figure = new PathFigure { StartPoint = PointAt(vertices[0]), IsClosed = closed, IsFilled = true };
            int segments = closed ? vertices.Count : vertices.Count - 1;
            for (int i = 0; i < segments; ++i)
            {
                int j = (i + 1) % vertices.Count;
                figure.Segments.Add(new BezierSegment { Point1 = Add(PointAt(vertices[i]), PointAt(shape["o"][i])),
                    Point2 = Add(PointAt(vertices[j]), PointAt(shape["i"][j])), Point3 = PointAt(vertices[j]) });
            }
            var geometry = new PathGeometry(); geometry.Figures.Add(figure); return geometry;
        }

        private static Canvas Shapes(JArray shapes, double frame, int depth)
        {
            var canvas = new Canvas();
            if (shapes == null || depth > 16) return canvas;
            JToken fill = shapes.LastOrDefault(s => (string)s["ty"] == "fl");
            JToken stroke = shapes.LastOrDefault(s => (string)s["ty"] == "st");
            foreach (JToken s in shapes.Reverse())
            {
                if ((bool?)s["hd"] == true) continue;
                string type = (string)s["ty"];
                if (type == "tr") { Transform(canvas, s, frame); continue; }
                if (type == "gr") { canvas.Children.Add(Shapes(s["it"] as JArray, frame, depth + 1)); continue; }
                Geometry geometry = null;
                if (type == "sh") geometry = PathGeometryFor(Value(s["ks"], frame));
                if (type == "el" || type == "rc")
                {
                    Point p = PointAt(Value(s["p"], frame)), size = PointAt(Value(s["s"], frame));
                    var rect = new Rect(p.X - size.X / 2, p.Y - size.Y / 2, Math.Max(0, size.X), Math.Max(0, size.Y));
                    geometry = type == "el" ? (Geometry)new EllipseGeometry { Center = p, RadiusX = size.X / 2, RadiusY = size.Y / 2 } :
                        new RectangleGeometry { Rect = rect, RadiusX = Component(Value(s["r"], frame), 0), RadiusY = Component(Value(s["r"], frame), 0) };
                }
                if (geometry != null) canvas.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Fill = BrushFor(fill, frame),
                    Stroke = BrushFor(stroke, frame), StrokeThickness = stroke == null ? 0 : Component(Value(stroke["w"], frame), 0), StrokeLineJoin = PenLineJoin.Round });
            }
            return canvas;
        }

        private static void AddLayer(Canvas root, JObject layer, JArray layers, double frame, int depth, JObject animation)
        {
            if (depth > 16 || (bool?)layer["hd"] == true || frame < Number(layer["ip"]) || frame >= Number(layer["op"], double.MaxValue)) return;
            double localFrame = (frame - Number(layer["st"])) / Math.Max(.001, Number(layer["sr"], 1));
            Canvas content = Shapes(layer["shapes"] as JArray, localFrame, 0);
            Transform(content, layer["ks"], frame);
            JToken parentId = layer["parent"];
            int guard = 0;
            while (parentId != null && ++guard < 16)
            {
                JObject parent = layers.OfType<JObject>().FirstOrDefault(l => Number(l["ind"]) == Number(parentId));
                if (parent == null) break;
                var wrapper = new Canvas(); wrapper.Children.Add(content); Transform(wrapper, parent["ks"], frame);
                // Lottie parenting inherits transforms, NOT parent opacity.
                wrapper.Opacity = 1; content = wrapper;
                parentId = parent["parent"];
            }
            if (Number(layer["ty"]) == 0)
            {
                JArray assets = animation["assets"] as JArray;
                JObject asset = assets == null ? null : assets.OfType<JObject>().FirstOrDefault(a => (string)a["id"] == (string)layer["refId"]);
                JArray childLayers = asset == null ? null : asset["layers"] as JArray;
                if (childLayers != null) foreach (JObject child in childLayers.Reverse().OfType<JObject>()) AddLayer(content, child, childLayers, localFrame, depth + 1, animation);
            }
            root.Children.Add(content);
        }

        private void RenderFrame()
        {
            if (_animation == null || !_loaded) return;
            try
            {
                var bitmap = Rasterize(_animation, RasterSize(), Number(_animation["ip"]));
                Source = bitmap;
                if (PreviewCache.Count >= 24) PreviewCache.Clear();
                PreviewCache[PreviewKey()] = bitmap;
            }
            catch (Exception ex) { Stop(); Logger.Instance.Info("Animated sticker render failed: {0}", ex.GetType().Name); }
        }

        internal static WriteableBitmap Rasterize(JObject animation, int pixels, double frame)
        {
                double w = Number(animation["w"], 512), h = Number(animation["h"], 512);
                var root = new Canvas { Width = pixels, Height = pixels, Clip = new RectangleGeometry { Rect = new Rect(0, 0, pixels, pixels) } };
                var scaled = new Canvas { RenderTransform = new ScaleTransform { ScaleX = pixels / Math.Max(1, w), ScaleY = pixels / Math.Max(1, h) } };
                root.Children.Add(scaled);
                JArray layers = (JArray)animation["layers"];
                foreach (JObject layer in layers.Reverse().OfType<JObject>()) AddLayer(scaled, layer, layers, frame, 0, animation);
                root.Measure(new Size(pixels, pixels)); root.Arrange(new Rect(0, 0, pixels, pixels));
#if DESKTOP_RENDER_TEST
                var bitmap = new WriteableBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32, null);
#else
                var bitmap = new WriteableBitmap(pixels, pixels);
#endif
                bitmap.Render(root, null); bitmap.Invalidate();
                return bitmap;
        }
    }
}
