using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VKClient.Audio.Base;
using VKClient.Audio.ViewModels;

namespace VKClient.Common.UC
{
  public class MiniPlayerUC : UserControl
  {
    internal Grid trackPanel;
    internal Grid miniPlayerRoot;
    internal TranslateTransform miniPlayerTranslate;
    private bool _contentLoaded;
    private bool _isSwipeGesture;
    private bool _isSwipeAnimating;

    private const double SwipeDistanceThreshold = 28.0;
    private const double SwipeVelocityThreshold = 500.0;
    private const double SwipeDismissDistance = 96.0;

    private AudioPlayerViewModel ViewModel
    {
      get
      {
        return base.DataContext as AudioPlayerViewModel;
      }
    }

    public MiniPlayerUC()
    {
      //base.\u002Ector();
      this.InitializeComponent();
      base.DataContext = (new AudioPlayerViewModel());
      // ISSUE: method pointer
      base.Loaded+=(delegate(object o, RoutedEventArgs e)
      {
          this.ViewModel.Activate(true);
      });
    }

    private void PlayButton_OnClicked(object sender, System.Windows.Input.GestureEventArgs e)
    {
      this.ViewModel.Play();
    }

    private void PauseButton_OnClicked(object sender, System.Windows.Input.GestureEventArgs e)
    {
      this.ViewModel.Pause();
    }

    private void MiniPlayer_OnManipulationStarted(object sender, ManipulationStartedEventArgs e)
    {
      if (this._isSwipeAnimating)
      {
        e.Handled = true;
        return;
      }
      this._isSwipeGesture = false;
    }

    private void MiniPlayer_OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
    {
      if (this._isSwipeAnimating || this.miniPlayerTranslate == null || this.miniPlayerRoot == null)
        return;

      Point cumulative = e.CumulativeManipulation.Translation;
      if (!this._isSwipeGesture)
      {
        if (cumulative.Y <= 4.0 || Math.Abs(cumulative.Y) <= Math.Abs(cumulative.X))
          return;
        this._isSwipeGesture = true;
      }

      Point delta = e.DeltaManipulation.Translation;
      double y = this.miniPlayerTranslate.Y + delta.Y;
      if (y < 0.0)
        y = 0.0;
      if (y > SwipeDismissDistance)
        y = SwipeDismissDistance;

      this.miniPlayerTranslate.Y = y;
      this.miniPlayerRoot.Opacity = 1.0 - Math.Min(0.58, y / SwipeDismissDistance * 0.58);
      e.Handled = true;
    }

    private void MiniPlayer_OnManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
    {
      if (this._isSwipeAnimating || this.miniPlayerTranslate == null || this.miniPlayerRoot == null)
        return;

      if (!this._isSwipeGesture)
        return;

      e.Handled = true;
      this._isSwipeGesture = false;

      double velocityY = 0.0;
      if (e.FinalVelocities != null)
        velocityY = e.FinalVelocities.LinearVelocity.Y;

      bool shouldDismiss = this.miniPlayerTranslate.Y >= SwipeDistanceThreshold || velocityY >= SwipeVelocityThreshold;
      if (shouldDismiss)
        this.AnimateSwipeDismiss();
      else
        this.AnimateSwipeBack();
    }

    private void AnimateSwipeDismiss()
    {
      this._isSwipeAnimating = true;
      Storyboard storyboard = new Storyboard();

      DoubleAnimation moveAnimation = new DoubleAnimation();
      moveAnimation.From = this.miniPlayerTranslate.Y;
      moveAnimation.To = SwipeDismissDistance;
      moveAnimation.Duration = TimeSpan.FromMilliseconds(180.0);
      moveAnimation.EasingFunction = new ExponentialEase()
      {
        EasingMode = EasingMode.EaseIn,
        Exponent = 4.0
      };
      Storyboard.SetTarget(moveAnimation, this.miniPlayerTranslate);
      Storyboard.SetTargetProperty(moveAnimation, new PropertyPath("Y"));

      DoubleAnimation opacityAnimation = new DoubleAnimation();
      opacityAnimation.From = this.miniPlayerRoot.Opacity;
      opacityAnimation.To = 0.0;
      opacityAnimation.Duration = TimeSpan.FromMilliseconds(180.0);
      Storyboard.SetTarget(opacityAnimation, this.miniPlayerRoot);
      Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));

      storyboard.Children.Add(moveAnimation);
      storyboard.Children.Add(opacityAnimation);
      storyboard.Completed += delegate(object sender, EventArgs args)
      {
        this.miniPlayerTranslate.Y = 0.0;
        this.miniPlayerRoot.Opacity = 1.0;
        this._isSwipeAnimating = false;
        BGAudioPlayerWrapper.Instance.Close();
      };
      storyboard.Begin();
    }

    private void AnimateSwipeBack()
    {
      this._isSwipeAnimating = true;
      Storyboard storyboard = new Storyboard();

      DoubleAnimation moveAnimation = new DoubleAnimation();
      moveAnimation.From = this.miniPlayerTranslate.Y;
      moveAnimation.To = 0.0;
      moveAnimation.Duration = TimeSpan.FromMilliseconds(190.0);
      moveAnimation.EasingFunction = new ExponentialEase()
      {
        EasingMode = EasingMode.EaseOut,
        Exponent = 5.0
      };
      Storyboard.SetTarget(moveAnimation, this.miniPlayerTranslate);
      Storyboard.SetTargetProperty(moveAnimation, new PropertyPath("Y"));

      DoubleAnimation opacityAnimation = new DoubleAnimation();
      opacityAnimation.From = this.miniPlayerRoot.Opacity;
      opacityAnimation.To = 1.0;
      opacityAnimation.Duration = TimeSpan.FromMilliseconds(190.0);
      Storyboard.SetTarget(opacityAnimation, this.miniPlayerRoot);
      Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));

      storyboard.Children.Add(moveAnimation);
      storyboard.Children.Add(opacityAnimation);
      storyboard.Completed += delegate(object sender, EventArgs args)
      {
        this.miniPlayerTranslate.Y = 0.0;
        this.miniPlayerRoot.Opacity = 1.0;
        this._isSwipeAnimating = false;
      };
      storyboard.Begin();
    }

    [DebuggerNonUserCode]
    public void InitializeComponent()
    {
      if (this._contentLoaded)
        return;
      this._contentLoaded = true;
      Application.LoadComponent(this, new Uri("/VKClient.Common;component/UC/MiniPlayerUC.xaml", UriKind.Relative));
      this.trackPanel = (Grid) base.FindName("trackPanel");
      this.miniPlayerRoot = (Grid) base.FindName("miniPlayerRoot");
      this.miniPlayerTranslate = (TranslateTransform) base.FindName("miniPlayerTranslate");
    }
  }
}
