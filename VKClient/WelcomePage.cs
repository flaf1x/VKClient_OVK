using Microsoft.Phone.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using VKClient.Common.Emoji;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.UC;

namespace VKClient.Common
{
  public class WelcomePage : PageBase
  {
    private bool _contentLoaded;
    private int _selectedInstanceIndex;
    private readonly List<InstanceChoice> _instanceChoices = new List<InstanceChoice>();
    internal TextBlock InstanceNameBlock;
    internal TextBlock InstanceAddressBlock;
    internal StackPanel InstanceTextPanel;
    internal TranslateTransform InstanceTextTransform;
    internal StackPanel CustomInstancePanel;
    internal TextBox CustomInstanceText;

    public WelcomePage()
    {
      this.InitializeComponent();
      this.SuppressMenu = true;
      this._manualHandleValidationParams = true;
      this.InitializeInstancePicker();
    }

    private void InitializeInstancePicker()
    {
      for (int i = 0; i < OpenVKInstanceManager.Presets.Count; i++)
      {
        OpenVKInstance preset = OpenVKInstanceManager.Presets[i];
        this._instanceChoices.Add(new InstanceChoice(preset.Name, preset.WebOrigin, preset));
      }
      this._instanceChoices.Add(new InstanceChoice("Custom instance", "enter your own server address", null));
      this._selectedInstanceIndex = this._instanceChoices.FindIndex(choice =>
        choice.Instance != null && choice.Instance.Id == OpenVKInstanceManager.Current.Id);
      if (this._selectedInstanceIndex < 0)
        this._selectedInstanceIndex = this._instanceChoices.Count - 1;
      if (OpenVKInstanceManager.Current.Id == "custom")
      {
        this.CustomInstancePanel.Visibility = Visibility.Visible;
        this.CustomInstanceText.Text = OpenVKInstanceManager.Current.WebOrigin;
      }
      this.UpdateInstanceSelector(false, 0);
    }

    private void PreviousInstance_Tap(object sender, System.Windows.Input.GestureEventArgs e)
    {
      this._selectedInstanceIndex = (this._selectedInstanceIndex - 1 + this._instanceChoices.Count) % this._instanceChoices.Count;
      this.UpdateInstanceSelector(true, -1);
    }

    private void NextInstance_Tap(object sender, System.Windows.Input.GestureEventArgs e)
    {
      this._selectedInstanceIndex = (this._selectedInstanceIndex + 1) % this._instanceChoices.Count;
      this.UpdateInstanceSelector(true, 1);
    }

    private void InstanceSelector_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
    {
      double horizontal = e.TotalManipulation.Translation.X;
      double vertical = e.TotalManipulation.Translation.Y;
      if (Math.Abs(horizontal) < 42 || Math.Abs(horizontal) <= Math.Abs(vertical))
        return;
      if (horizontal < 0)
        this.NextInstance_Tap(sender, null);
      else
        this.PreviousInstance_Tap(sender, null);
    }

    private void UpdateInstanceSelector(bool applySelection, int direction)
    {
      InstanceChoice choice = this._instanceChoices[this._selectedInstanceIndex];
      this.InstanceNameBlock.Text = choice.Title;
      this.InstanceAddressBlock.Text = choice.Instance == null
        ? "other server"
        : choice.Subtitle;
      this.CustomInstancePanel.Visibility = choice.Instance == null ? Visibility.Visible : Visibility.Collapsed;
      if (applySelection && choice.Instance != null)
        OpenVKInstanceManager.Select(choice.Instance);
      if (direction != 0)
        this.AnimateInstanceText(direction);
    }

    private void AnimateInstanceText(int direction)
    {
      this.InstanceTextPanel.Opacity = 0;
      this.InstanceTextTransform.X = direction > 0 ? 42 : -42;
      Storyboard storyboard = new Storyboard();
      DoubleAnimation slide = new DoubleAnimation
      {
        From = this.InstanceTextTransform.X,
        To = 0,
        Duration = TimeSpan.FromMilliseconds(180),
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
      };
      DoubleAnimation fade = new DoubleAnimation
      {
        From = 0,
        To = 1,
        Duration = TimeSpan.FromMilliseconds(150)
      };
      Storyboard.SetTarget(slide, this.InstanceTextTransform);
      Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.XProperty));
      Storyboard.SetTarget(fade, this.InstanceTextPanel);
      Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
      storyboard.Children.Add(slide);
      storyboard.Children.Add(fade);
      storyboard.Begin();
    }

    private void ApplyCustomInstance_Click(object sender, RoutedEventArgs e)
    {
      OpenVKInstance instance;
      if (this.TryApplyCustomInstance(out instance))
      {
        MessageBox.Show("Selected " + instance.WebOrigin, "Instance", MessageBoxButton.OK);
      }
    }

    private void CustomInstanceText_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key != Key.Enter)
        return;
      OpenVKInstance instance;
      this.TryApplyCustomInstance(out instance);
      this.Focus();
    }

    private void CustomInstanceText_LostFocus(object sender, RoutedEventArgs e)
    {
      string value = (this.CustomInstanceText.Text ?? string.Empty).Trim();
      if (value.Length == 0 || value == "https://")
        return;
      OpenVKInstance instance;
      this.TryApplyCustomInstance(out instance);
    }

    private bool TryApplyCustomInstance(out OpenVKInstance instance)
    {
      string error;
      if (!OpenVKInstanceManager.TryCreateCustom(this.CustomInstanceText.Text, out instance, out error))
      {
        MessageBox.Show(error, "Instance", MessageBoxButton.OK);
        return false;
      }
      OpenVKInstanceManager.Select(instance);
      return true;
    }

    protected override void HandleOnNavigatedTo(NavigationEventArgs e)
    {
      base.HandleOnNavigatedTo(e);
      base.DataContext = new ViewModelBase();
      base.NavigationService.ClearBackStack();
      PushNotificationsManager.Instance.EnsureTheChannelIsClosed();
      AppGlobalStateManager.Current.HandleUserLogoutOrAuthorizationFailure();
      StickersSettings.Instance.Reset();
      SearchHintsUC.Reset();
      ContactsManager.Instance.DeleteAllContactsAsync();
      TileManager.Instance.UpdateTileWithCount(0, new Action(() => { }));
      NewsViewModel.Instance.Reset();
      WebBrowserExtensions.ClearCookiesAsync(new WebBrowser());
      BaseDataManager.Instance.NeedRefreshBaseData = true;
    }

    private void OnLogInClicked(object sender, RoutedEventArgs e)
    {
      OpenVKInstance custom;
      if (this.CustomInstancePanel.Visibility == Visibility.Visible && !this.TryApplyCustomInstance(out custom))
        return;
      this.NavigationService.Navigate(new Uri("/LoginPage.xaml", UriKind.Relative));
    }

    private void OnSignUpClicked(object sender, RoutedEventArgs e)
    {
      OpenVKInstance custom;
      if (this.CustomInstancePanel.Visibility == Visibility.Visible && !this.TryApplyCustomInstance(out custom))
        return;
      Navigator.Current.NavigateToWebUri(VKConstants.OpenVKWebAddress + "/reg", true, false);
    }

    private void OnTermsClicked(object sender, System.Windows.Input.GestureEventArgs e)
    {
      Navigator.Current.NavigateToWebUri(VKConstants.OpenVKWebAddress + "/terms", true, false);
    }

    [DebuggerNonUserCode]
    public void InitializeComponent()
    {
      if (this._contentLoaded)
        return;
      this._contentLoaded = true;
      Application.LoadComponent(this, new Uri("/VKClient;component/WelcomePage.xaml", UriKind.Relative));
      this.InstanceNameBlock = (TextBlock)base.FindName("InstanceNameBlock");
      this.InstanceAddressBlock = (TextBlock)base.FindName("InstanceAddressBlock");
      this.InstanceTextPanel = (StackPanel)base.FindName("InstanceTextPanel");
      this.InstanceTextTransform = (TranslateTransform)base.FindName("InstanceTextTransform");
      this.CustomInstancePanel = (StackPanel)base.FindName("CustomInstancePanel");
      this.CustomInstanceText = (TextBox)base.FindName("CustomInstanceText");
    }

    private sealed class InstanceChoice
    {
      public string Title { get; private set; }
      public string Subtitle { get; private set; }
      public OpenVKInstance Instance { get; private set; }
      public InstanceChoice(string title, string subtitle, OpenVKInstance instance)
      {
        this.Title = title;
        this.Subtitle = subtitle;
        this.Instance = instance;
      }
      public override string ToString() { return this.Title; }
    }
  }
}
