using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Phone.Tasks;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Localization;
using VKClient.Common.UC;
using VKClient.Common.Utils;

namespace VKClient.Common
{
    public class ChatBackgroundPage : PageBase
    {
        // Messenger supplies the same virtualized rows used by ConversationPage.
        public static Func<Panel, bool, IDisposable> PreviewFactory { get; set; }
        private IDisposable _conversationPreview;
        private bool? _previewWallpaper;
        private bool _buildingPreview;
        private readonly PhotoChooserTask _chooser = new PhotoChooserTask { ShowCamera = true };
        private Grid _viewport;
        private Image _photo;
        private System.Windows.Shapes.Rectangle _dim;
        private Slider _zoomSlider, _dimSlider;
        private CheckBox _blurCheck;
        private Button _apply, _choose, _reset;
        private Button _adjustButton;
        private Border _adjustmentsPanel;
        private bool _adjustmentsOpen;
        private WriteableBitmap _source, _blurred;
        private byte[] _bytes;
        private string _key, _origin;
        private long _owner;
        private bool _initialized, _busy, _resetPending, _global, _suppress;
        private int _generation;
        private int _blurLoadingGeneration = -1;
        private double _x, _y;
        private double _savedPanX, _savedPanY;
        private bool _restorePan;

        public ChatBackgroundPage()
        {
            Application.LoadComponent(this, new Uri("/VKClient.Common;component/chatbackgroundpage.xaml", UriKind.Relative));
            SuppressMenu = true;
            ((GenericHeaderUC)FindName("Header")).HideSandwitchButton = true;
            _viewport = (Grid)FindName("PreviewViewport"); _photo = (Image)FindName("PreviewPhoto");
            _dim = (System.Windows.Shapes.Rectangle)FindName("PreviewDim");
            _zoomSlider = (Slider)FindName("ZoomSlider"); _dimSlider = (Slider)FindName("DimSlider"); _blurCheck = (CheckBox)FindName("BlurCheck");
            _apply = (Button)FindName("ApplyButton"); _choose = (Button)FindName("ChooseButton"); _reset = (Button)FindName("ResetButton");
            _adjustButton = (Button)FindName("AdjustButton"); _adjustmentsPanel = (Border)FindName("AdjustmentsPanel");
            SetAdjustmentsVisible(false);
            ((GenericHeaderUC)FindName("Header")).textBlockTitle.Text = CommonResources.ChatBackgroundTitle.ToUpperInvariant();
            _chooser.Completed += PhotoCompleted;
        }

        protected override void HandleOnNavigatedTo(NavigationEventArgs e)
        {
            base.HandleOnNavigatedTo(e);
            if (!_initialized)
            {
                _owner = AppGlobalStateManager.Current.LoggedInUserId; _origin = OpenVKInstanceManager.Current.ApiOrigin;
                _global = NavigationContext.QueryString.ContainsKey("all");
                ((GenericHeaderUC)FindName("Header")).textBlockTitle.Text = (_global ? CommonResources.ChatBackgroundAll : CommonResources.ChatBackgroundTitle).ToUpperInvariant();
                _key = _global ? ChatWallpaperStore.CurrentGlobalKey : ChatWallpaperStore.Key(_origin, _owner,
                    long.Parse(NavigationContext.QueryString["peer"]), bool.Parse(NavigationContext.QueryString["chat"]));
                if (State.ContainsKey("Wallpaper.Key") && (string)State["Wallpaper.Key"] != _key) { NavigationService.GoBack(); return; }
                _initialized = true; _suppress = true;
                _reset.Content = CommonResources.ChatBackgroundRemove;
                try
                {
                    var existing = ChatWallpaperStore.LoadEffective(_key);
                    _dimSlider.Value = existing == null ? .48 : existing.Dim;
                    if (existing != null)
                    {
                        _zoomSlider.Value = existing.Zoom; _blurCheck.IsChecked = existing.Blur;
                        _savedPanX = existing.PanX; _savedPanY = existing.PanY; _restorePan = true;
                    }
                    if (_bytes == null) _bytes = State.ContainsKey("Wallpaper.Key") ? ChatWallpaperStore.LoadDraft(_key) : (existing == null ? null : existing.OriginalPhoto ?? existing.Photo);
                    if (State.ContainsKey("Wallpaper.Zoom"))
                    {
                        _zoomSlider.Value = (double)State["Wallpaper.Zoom"]; _dimSlider.Value = (double)State["Wallpaper.Dim"];
                        _x = (double)State["Wallpaper.X"]; _y = (double)State["Wallpaper.Y"];
                        _restorePan = false;
                        _blurCheck.IsChecked = (bool)State["Wallpaper.Blur"]; _resetPending = (bool)State["Wallpaper.Reset"];
                        if (State.ContainsKey("Wallpaper.Adjustments")) SetAdjustmentsVisible((bool)State["Wallpaper.Adjustments"]);
                    }
                    SetPhoto(_bytes, false);
                }
                catch (Exception ex) { Report(ex); }
                finally { _suppress = false; }
                UpdatePreview(); EnsureBlur();
            }
        }

        private bool SameAccount { get { return _owner == AppGlobalStateManager.Current.LoggedInUserId && _origin == OpenVKInstanceManager.Current.ApiOrigin; } }
        private void ChoosePhotoClick(object sender, RoutedEventArgs e)
        {
            if (_busy || !SameAccount) return;
            try { ChatWallpaperStore.BeginSelection(_key); _chooser.Show(); }
            catch (Exception ex) { string ignored; ChatWallpaperStore.TakeSelection(out ignored); Report(ex); }
        }

        private void PhotoCompleted(object sender, PhotoResult result)
        {
            string key;
            if (!ChatWallpaperStore.TakeSelection(out key) || key == null || result.TaskResult != TaskResult.OK)
            { if (result.ChosenPhoto != null) result.ChosenPhoto.Close(); return; }
            _busy = true; UpdateButtons();
            ImagePreprocessor.PrepareChatBackground(result.ChosenPhoto, bytes => Execute.ExecuteOnUIThread(() => {
                _busy = false;
                try
                {
                    if (bytes == null) throw new IOException("Photo preparation failed");
                    // Only the draft changes here. Apply is the sole commit.
                    ChatWallpaperStore.SaveDraft(key, bytes);
                    if (_key != null && key != _key) return;
                    _bytes = bytes; _resetPending = false;
                    SetPhoto(bytes, true);
                    SetAdjustmentsVisible(false);
                }
                catch (Exception ex) { Report(ex); }
                UpdateButtons();
            }));
        }

        private void SetPhoto(byte[] bytes, bool resetCrop)
        {
            ++_generation; _blurred = null; _source = null; _photo.Source = null;
            if (bytes != null) using (var stream = new MemoryStream(bytes)) { var image = new BitmapImage(); image.SetSource(stream); _source = new WriteableBitmap(image); }
            if (resetCrop) { bool suppress = _suppress; _suppress = true; _restorePan = false; _x = _y = 0; _zoomSlider.Value = 1; _suppress = suppress; }
            _photo.Source = _source; UpdatePreview(); if (!_suppress) EnsureBlur();
        }

        private void EnsureBlur()
        {
            if (_source == null || _blurCheck.IsChecked != true) { _photo.Source = _source; UpdateButtons(); return; }
            if (_blurred != null) { _photo.Source = _blurred; UpdateButtons(); return; }
            if (_blurLoadingGeneration == _generation) return;
            int generation = _generation, width = _source.PixelWidth, height = _source.PixelHeight;
            _blurLoadingGeneration = generation;
            var pixels = (int[])_source.Pixels.Clone(); _busy = true; UpdateButtons();
            ThreadPool.QueueUserWorkItem(o => {
                int[] blurred = null; Exception failure = null;
                try { blurred = ChatWallpaperEditing.Blur(pixels, width, height, 8); } catch (Exception ex) { failure = ex; }
                Execute.ExecuteOnUIThread(() => {
                    if (generation != _generation) return;
                    _blurLoadingGeneration = -1;
                    _busy = false;
                    if (failure != null) { Report(failure); _blurCheck.IsChecked = false; }
                    else { _blurred = new WriteableBitmap(width, height); Array.Copy(blurred, _blurred.Pixels, blurred.Length); _blurred.Invalidate(); _photo.Source = _blurCheck.IsChecked == true ? _blurred : _source; }
                    UpdateButtons();
                });
            });
        }

        private void PreviewSizeChanged(object sender, SizeChangedEventArgs e) { if (_viewport != null) UpdatePreview(); }
        private void AppearanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_initialized && !_suppress) { if (_source != null) _resetPending = false; UpdatePreview(); } }
        private void BlurChanged(object sender, RoutedEventArgs e) { if (_initialized && !_suppress) { if (_source != null) _resetPending = false; EnsureBlur(); } }
        private void PreviewManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            e.Handled = true;
            if (_source == null || _busy) return;
            _resetPending = false;
            _x += e.DeltaManipulation.Translation.X; _y += e.DeltaManipulation.Translation.Y;
            UpdatePreview();
        }
        private void PreviewManipulationStarted(object sender, ManipulationStartedEventArgs e) { e.Handled = true; }
        private void PreviewManipulationCompleted(object sender, ManipulationCompletedEventArgs e) { e.Handled = true; }

        private void AdjustClick(object sender, RoutedEventArgs e) { if (!_busy) SetAdjustmentsVisible(!_adjustmentsOpen); }
        private void SetAdjustmentsVisible(bool visible)
        {
            _adjustmentsOpen = visible;
            _adjustmentsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _adjustButton.Content = visible ? CommonResources.ChatBackgroundPreview : CommonResources.ChatBackgroundAdjust;
        }

        protected override void InitializeProgressIndicator() { _progressBar.Visibility = Visibility.Collapsed; }

        private void UpdatePreview()
        {
            if (_viewport == null || _dimSlider == null) return;
            double w = _viewport.ActualWidth, h = _viewport.ActualHeight;
            _viewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, w, h) };
            _dim.Opacity = _dimSlider.Value;
            if (_source != null && w > 0 && h > 0)
            {
                double scale = Math.Max(w / _source.PixelWidth, h / _source.PixelHeight) * _zoomSlider.Value;
                _photo.Width = _source.PixelWidth * scale; _photo.Height = _source.PixelHeight * scale;
                if (_restorePan) { _x = _savedPanX * Math.Max(0, _photo.Width - w) / 2; _y = _savedPanY * Math.Max(0, _photo.Height - h) / 2; _restorePan = false; }
                _x = ChatWallpaperEditing.ClampOffset(_x, _photo.Width - w); _y = ChatWallpaperEditing.ClampOffset(_y, _photo.Height - h);
                var transform = (TranslateTransform)_photo.RenderTransform; transform.X = _x; transform.Y = _y;
                Canvas.SetLeft(_photo, (w - _photo.Width) / 2); Canvas.SetTop(_photo, (h - _photo.Height) / 2);
            }
            bool hasWallpaper = _source != null;
            if (_initialized && !_buildingPreview && PreviewFactory != null && _previewWallpaper != hasWallpaper)
            {
                // NewsTextItem measures itself using UpdateLayout. SizeChanged
                // can reenter here while native example rows are still loading.
                _buildingPreview = true;
                var host = (Panel)FindName("ConversationPreview");
                try
                {
                    if (_conversationPreview != null) _conversationPreview.Dispose();
                    _conversationPreview = null; host.Children.Clear();
                    _previewWallpaper = hasWallpaper;
                    _conversationPreview = PreviewFactory(host, hasWallpaper);
                }
                catch (Exception ex) { _previewWallpaper = null; host.Children.Clear(); Report(ex); }
                finally { _buildingPreview = false; }
            }
            UpdateButtons();
        }
        private void UpdateButtons()
        {
            if (_apply == null) return;
            _choose.IsEnabled = !_busy; _reset.IsEnabled = !_busy && _key != null && ChatWallpaperStore.Exists(_key);
            _adjustButton.IsEnabled = !_busy;
            _apply.IsEnabled = !_busy && (_source != null || _resetPending);
            _zoomSlider.IsEnabled = _dimSlider.IsEnabled = _blurCheck.IsEnabled = !_busy && _source != null;
        }

        private void ResetClick(object sender, RoutedEventArgs e)
        {
            if (_busy || !SameAccount) return;
            try
            {
                var inherited = _global ? null : ChatWallpaperStore.LoadWallpaper(ChatWallpaperStore.CurrentGlobalKey);
                _bytes = inherited == null ? null : inherited.OriginalPhoto ?? inherited.Photo;
                _dimSlider.Value = inherited == null ? .48 : inherited.Dim;
                _suppress = true;
                _blurCheck.IsChecked = inherited != null && inherited.Blur; _zoomSlider.Value = inherited == null ? 1 : inherited.Zoom;
                _savedPanX = inherited == null ? 0 : inherited.PanX; _savedPanY = inherited == null ? 0 : inherited.PanY; _restorePan = true;
                _resetPending = true; SetPhoto(_bytes, false); _suppress = false; EnsureBlur();
                SetAdjustmentsVisible(false);
            }
            catch (Exception ex) { Report(ex); }
            finally { _suppress = false; }
        }

        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            if (_busy || !SameAccount) return;
            if (_resetPending)
            {
                try { ChatWallpaperStore.Reset(_key); Finish(); } catch (Exception ex) { Report(ex); }
                return;
            }
            if (_source == null || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return;
            _busy = true; UpdateButtons();
            try
            {
                var bitmap = _blurCheck.IsChecked == true && _blurred != null ? _blurred : _source;
                var crop = ChatWallpaperEditing.Crop(bitmap.PixelWidth, bitmap.PixelHeight, _viewport.ActualWidth, _viewport.ActualHeight, _zoomSlider.Value, _x, _y);
                var cropped = bitmap.Crop(crop); var size = ImagePreprocessor.GetChatBackgroundSize(cropped.PixelWidth, cropped.PixelHeight);
                string key = _key; double dim = _dimSlider.Value;
                byte[] original = _bytes; double zoom = _zoomSlider.Value;
                double panX = _photo.Width <= _viewport.ActualWidth ? 0 : _x * 2 / (_photo.Width - _viewport.ActualWidth);
                double panY = _photo.Height <= _viewport.ActualHeight ? 0 : _y * 2 / (_photo.Height - _viewport.ActualHeight);
                bool blur = _blurCheck.IsChecked == true;
                ThreadPool.QueueUserWorkItem(o => {
                    byte[] bytes = null; Exception failure = null;
                    try { using (var output = new MemoryStream()) { System.Windows.Media.Imaging.Extensions.SaveJpeg(cropped, output, (int)size.Width, (int)size.Height, 0, 85); bytes = output.ToArray(); } }
                    catch (Exception ex) { failure = ex; }
                    Execute.ExecuteOnUIThread(() => {
                        _busy = false;
                        try { if (failure != null) throw failure; if (!SameAccount) return; ChatWallpaperStore.Save(key, bytes, dim, original, zoom, Math.Max(-1, Math.Min(1, panX)), Math.Max(-1, Math.Min(1, panY)), blur); Finish(); }
                        catch (Exception ex) { Report(ex); }
                        UpdateButtons();
                    });
                });
            }
            catch (Exception ex) { _busy = false; Report(ex); UpdateButtons(); }
        }

        private void Finish()
        {
            try { ChatWallpaperStore.ClearDraft(_key); } catch (Exception ex) { Logger.Instance.Info("Wallpaper draft cleanup failed: {0}", ex.GetType().Name); }
            NavigationService.GoBack();
        }
        private void Report(Exception ex) { Logger.Instance.Info("Wallpaper editor failed: {0}", ex.GetType().Name); new GenericInfoUC().ShowAndHideLater(CommonResources.Error, null); }
        protected override void HandleOnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.HandleOnNavigatingFrom(e);
            if (_busy && e.IsNavigationInitiator) e.Cancel = true;
        }
        protected override void HandleOnNavigatedFrom(NavigationEventArgs e)
        {
            if (_initialized)
            {
                State["Wallpaper.Key"] = _key; State["Wallpaper.Zoom"] = _zoomSlider.Value; State["Wallpaper.Dim"] = _dimSlider.Value;
                State["Wallpaper.X"] = _x; State["Wallpaper.Y"] = _y; State["Wallpaper.Blur"] = _blurCheck.IsChecked == true; State["Wallpaper.Reset"] = _resetPending;
                State["Wallpaper.Adjustments"] = _adjustmentsOpen;
                try { if (_bytes != null) ChatWallpaperStore.SaveDraft(_key, _bytes); } catch (Exception ex) { Report(ex); }
            }
            base.HandleOnNavigatedFrom(e);
        }
        protected override void OnRemovedFromJournal(JournalEntryRemovedEventArgs e)
        {
            ++_generation;
            if (_key != null) try { ChatWallpaperStore.ClearDraft(_key); } catch (Exception) { }
            _source = _blurred = null; _photo.Source = null; _bytes = null;
            if (_conversationPreview != null) { _conversationPreview.Dispose(); _conversationPreview = null; }
            base.OnRemovedFromJournal(e);
        }
    }
}
