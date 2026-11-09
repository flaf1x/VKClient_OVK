using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VKClient.Common.Framework;
using VKClient.Common.Localization;
using VKClient.Common.Utils;

namespace VKClient.Common.Library.VirtItems
{
  public class UnreadItem : VirtualizableItemBase
  {
    private double _verticalWidth;
    private double _horizontalWidth;
    private bool _isHorizontal;
    private Grid _row;

    public bool IsHorizontal
    {
      get
      {
        return this._isHorizontal;
      }
      set
      {
        if (this._isHorizontal == value)
          return;
        this._isHorizontal = value;
        this.Width = this._isHorizontal ? this._horizontalWidth : this._verticalWidth;
        if (_row != null) _row.Width = this.Width;
      }
    }

    public override double FixedHeight
    {
      get
      {
        return 31.0;
      }
    }

    public UnreadItem(double verticalWidth, Thickness margin, bool isHorizontal, double horizontalWidth)
        : base(verticalWidth, margin, new Thickness())
    {
      this._verticalWidth = verticalWidth;
      this._horizontalWidth = horizontalWidth;
      this._isHorizontal = isHorizontal;
      this.Width = this._isHorizontal ? this._horizontalWidth : this._verticalWidth;
    }

    protected override void GenerateChildren()
    {
      base.GenerateChildren();
      var foreground = Application.Current.Resources["PhoneVKSubtleBrush"] as Brush;
      var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
      row.Children.Add(new TextBlock { Text = CommonResources.UNREADMESSAGES.ToUpperInvariant(), FontFamily = new FontFamily("Segoe WP Semibold"), FontSize = 20, Foreground = foreground });
      string uri = MultiResolutionHelper.Instance.AppendResolutionSuffix("/VKClient.Common;component/Resources/msgs-unreadarrow.png", false, "");
      row.Children.Add(new Border { Width = 13, Height = 16, Margin = new Thickness(10, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Background = foreground, OpacityMask = new ImageBrush { ImageSource = new BitmapImage(new Uri(uri, UriKind.Relative)) } });
      var grid = new Grid { Width = this.Width, Height = this.FixedHeight };
      grid.ColumnDefinitions.Add(new ColumnDefinition());
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      grid.ColumnDefinitions.Add(new ColumnDefinition());
      Grid.SetColumn(row, 1); grid.Children.Add(row);
      for (int side = 0; side < 2; side++)
      {
        var line = new Rectangle { Height = 3, VerticalAlignment = VerticalAlignment.Center,
          Margin = side == 0 ? new Thickness(-18, 0, 12, 0) : new Thickness(12, 0, -18, 0),
          Fill = Application.Current.Resources["PhoneDialogInMessageBackgroundBrush"] as Brush };
        Grid.SetColumn(line, side == 0 ? 0 : 2); grid.Children.Add(line);
      }
      _row = grid;
      this.Children.Add(grid);
    }
  }
}
