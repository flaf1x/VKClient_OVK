using System;
using System.IO.IsolatedStorage;
using System.Windows.Threading;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Library;

namespace VKClient.Common.Library
{
  /// <summary>
  /// OpenVK does not expose the legacy VK/MPNS device registration methods.
  /// Keep the original settings surface, but deliver notifications by polling
  /// notifications.get in foreground and from the existing periodic agent.
  /// </summary>
  public class PushNotificationsManager
  {
    private const string OpenVkDefaultsKey =
      "OpenVK.Notifications.FeedbackDefaultsV1";
    private static PushNotificationsManager _instance;
    private DispatcherTimer _timer;
    private bool _initialized;
    private bool _legacyChannelClosed;
    private bool _checkPending;
    private DateTime _lastCheckUtc;

    public static PushNotificationsManager Instance
    {
      get
      {
        if (_instance == null)
          _instance = new PushNotificationsManager();
        return _instance;
      }
    }

    public void Initialize()
    {
      if (AppGlobalStateManager.Current.LoggedInUserId == 0L)
        return;

      EnsureOpenVkNotificationDefaults();

      if (!_legacyChannelClosed)
      {
        PushChannelManager.Instance.CloseChannel();
        _legacyChannelClosed = true;
      }

      if (!_initialized)
      {
        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromSeconds(15.0);
        _timer.Tick += (s, e) => CheckNow();
        _initialized = true;
      }

      if (AppGlobalStateManager.Current.GlobalState.PushNotificationsEnabled)
        _timer.Start();
      else
        _timer.Stop();

      if (_lastCheckUtc == default(DateTime) ||
          DateTime.UtcNow - _lastCheckUtc >= _timer.Interval)
        CheckNow();
    }

    private static void EnsureOpenVkNotificationDefaults()
    {
      if (IsolatedStorageSettings.ApplicationSettings.Contains(
          OpenVkDefaultsKey))
        return;

      AppGlobalStateData globalState =
        AppGlobalStateManager.Current.GlobalState;
      if (globalState.PushSettings == null)
        globalState.PushSettings = new PushSettings();

      globalState.PushNotificationsEnabled = true;
      globalState.PushSettings.friend = true;
      globalState.PushSettings.like = true;
      globalState.PushSettings.repost = true;
      globalState.PushSettings.wall_post = true;
      globalState.PushSettings.comment = true;
      globalState.PushSettings.mention = true;
      globalState.PushSettings.reply = true;

      IsolatedStorageSettings.ApplicationSettings[OpenVkDefaultsKey] = true;
      IsolatedStorageSettings.ApplicationSettings.Save();
      AppGlobalStateManager.Current.SaveState();
    }

    private void CheckNow()
    {
      if (_checkPending)
        return;
      _checkPending = true;
      _lastCheckUtc = DateTime.UtcNow;
      OpenVKNotificationsManager.CheckForUpdates(false,
        (Action<bool>)(hasUpdates =>
        {
          _checkPending = false;
          if (hasUpdates)
            CountersManager.Current.RefreshCounters();
        }));
    }

    public void EnsureTheChannelIsClosed()
    {
      if (_timer != null)
        _timer.Stop();
      PushChannelManager.Instance.CloseChannel();
      AppGlobalStateManager.Current.GlobalState.RegisteredDeviceId = "";
      _initialized = false;
    }

    public void UpdateDeviceRegistration(Action<bool> resultCallback)
    {
      bool enabled =
        AppGlobalStateManager.Current.GlobalState.PushNotificationsEnabled;
      AppGlobalStateManager.Current.GlobalState.RegisteredDeviceId =
        enabled ? "openvk-polling" : "";

      if (enabled)
        Initialize();
      else if (_timer != null)
        _timer.Stop();

      if (resultCallback != null)
        resultCallback(true);
    }
  }
}
