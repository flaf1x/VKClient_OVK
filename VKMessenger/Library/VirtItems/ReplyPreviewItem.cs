using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VKClient.Common.Framework;
using VKMessenger.Library.Events;

namespace VKMessenger.Library.VirtItems
{
    public sealed class ReplyPreviewItem : VirtualizableItemBase
    {
        private readonly MessageViewModel _owner;
        private TextBlock _author;
        public override double FixedHeight { get { return 60; } }
        public ReplyPreviewItem(double width, Thickness margin, MessageViewModel owner) : base(width, margin) { _owner = owner; }
        protected override void GenerateChildren()
        {
            base.GenerateChildren();
            var accent = Application.Current.Resources["PhoneNameBlueBrush"] as Brush;
            var grid = new Grid { Width = Width, Height = FixedHeight, Background = new SolidColorBrush(Colors.Transparent) };
            grid.Children.Add(new Border { Width = 3, Background = accent, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5) });
            _author = new TextBlock { Text = ReplyPresentation.Author(_owner.Message.reply_message), FontSize = 20,
                FontFamily = new FontFamily("Segoe WP Semibold"), Foreground = accent, Margin = new Thickness(12, 0, 4, 0), TextTrimming = TextTrimming.WordEllipsis };
            grid.Children.Add(_author);
            grid.Children.Add(new TextBlock { Text = ReplyPresentation.Summary(_owner.Message.reply_message), FontSize = 20,
                Foreground = Application.Current.Resources["PhoneForegroundBrush"] as Brush, Opacity = 0.75,
                Margin = new Thickness(12, 28, 4, 0), TextTrimming = TextTrimming.WordEllipsis });
            grid.Tap += QuoteTap;
            Children.Add(grid);
            _owner.PropertyChanged += OwnerChanged;
        }
        private void OwnerChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "UIUserName" && _author != null) _author.Text = ReplyPresentation.Author(_owner.Message.reply_message);
        }
        private void QuoteTap(object sender, GestureEventArgs e)
        {
            e.Handled = true;
            EventAggregator.Current.Publish(new MessageActionEvent { Message = _owner, MessageActionType = MessageActionType.GoToReply });
        }
        protected override void ReleaseResourcesOnUnload()
        {
            _owner.PropertyChanged -= OwnerChanged;
            _author = null;
            base.ReleaseResourcesOnUnload();
        }
    }
}
