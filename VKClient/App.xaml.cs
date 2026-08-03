using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.IsolatedStorage;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using VKClient.Audio.Base;
using VKClient.Audio.Base.AudioCache;
using VKClient.Audio.Base.Core;
using VKClient.Audio.Base.DataObjects;
using VKClient.Audio.Base.Library;
using VKClient.Audio.Base.Social;
using VKClient.Common.AudioManager;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Library.Events;
using VKClient.Common.Library.Games;
using VKClient.Common.Stickers.AutoSuggest;
using VKClient.Common.Utils;
using VKClient.Common.VideoCatalog;
using VKClient.Library;
using VKClient.Video.VideoCatalog;
using VKMessenger;
using VKMessenger.Library;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;

namespace VKClient
{
    public partial class App : Application, IAppStateInfo
    {
        private static string _imageDictionaryKey = "ImageDict";
        //public static TelemetryClient TelemetryClient;
        private static CustomUriMapper _uriMapper;
        private bool phoneApplicationInitialized;
    //    private bool _wasReset;
        private bool _handlingPreLoginNavigation;
        private App.SessionType sessionType;
        private bool wasRelaunched;

        public PhoneApplicationFrame RootFrame { get; private set; }

        public ShareOperation ShareOperation { get; set; }

        public StartState StartState { get; private set; }

        private IsolatedStorageSettings Settings
        {
            get
            {
                return IsolatedStorageSettings.ApplicationSettings;
            }
        }

        public App()
        {
            //base.\u002Ector();
            //this.InitializeTelemetry();
            AppendEarlyStartupMarker("01 App constructor entered");
            Logger.Instance.Info("App() check 1");
            this.UnhandledException += (new EventHandler<ApplicationUnhandledExceptionEventArgs>(this.App_UnhandledException));
            AppendEarlyStartupMarker("02 before InitializeComponent");
            this.InitializeComponent();
            AppendEarlyStartupMarker("03 after InitializeComponent");
            WriteOpenVkBuildMarker();
            Logger.Instance.Info("App() check 2");
            ThemeSettings themeSettings = ThemeSettingsManager.GetThemeSettings();
            AppendEarlyStartupMarker("04 theme settings loaded");
            Logger.Instance.Info("App() check 3");
            this.ApplyThemeBasedOnSettings(themeSettings);
            AppendEarlyStartupMarker("05 theme applied");
            Logger.Instance.Info("App() check 4");
            this.InitializePhoneApplication();
            AppendEarlyStartupMarker("06 phone application initialized");
            Logger.Instance.Info("App() check 5");
            this.InitializeLanguage(themeSettings);
            AppendEarlyStartupMarker("07 language initialized");
            Logger.Instance.Info("App() check 6");
            this.InitializeServiceLocator();
            AppendEarlyStartupMarker("08 service locator initialized");
            Logger.Instance.Info("App() check 7");
            IPageDataRequesteeInfo pageDataRequestee = PageBase.CurrentPageDataRequestee;
        }

        private static void AppendEarlyStartupMarker(string stage)
        {
            try
            {
                using (IsolatedStorageFile store = IsolatedStorageFile.GetUserStoreForApplication())
                using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(
                    "StartupTrace.txt", FileMode.Append, FileAccess.Write, store))
                using (StreamWriter writer = new StreamWriter(stream))
                    writer.WriteLine(DateTime.UtcNow.ToString("O") + " " + stage);
            }
            catch
            {
            }
        }

        private static void WriteOpenVkBuildMarker()
        {
            try
            {
                using (IsolatedStorageFile store = IsolatedStorageFile.GetUserStoreForApplication())
                using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(
                    "OpenVKBuild.txt",
                    System.IO.FileMode.Create,
                    System.IO.FileAccess.Write,
                    store))
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(stream))
                    writer.Write("4.98-diagnostic");
            }
            catch
            {
                // A marker is diagnostic only and must never block startup.
            }
        }

        private void InitializeLanguage(ThemeSettings settings)
        {
            try
            {
                string languageCultureString = settings.LanguageCultureString;
                if (languageCultureString != string.Empty)
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(languageCultureString);
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(languageCultureString);
                    CultureInfo.DefaultThreadCurrentCulture = (new CultureInfo(languageCultureString));
                    CultureInfo.DefaultThreadCurrentUICulture = (new CultureInfo(languageCultureString));
                }
                AppliedSettingsInfo.AppliedLanguageSetting = settings.LanguageSettings;
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("App.InitializeLanguage failed", ex);
                if (Debugger.IsAttached)
                    Debugger.Break();
                throw;
            }
        }

        private void ApplyThemeBasedOnSettings(ThemeSettings settings)
        {
            Logger.Instance.Info("App.ApplyThemeBasedOnSettings check 1");
            ThemeManager.OverrideOptions = ThemeManagerOverrideOptions.None;
            if (settings.BackgroundSettings == 1)
                settings.BackgroundSettings = 3;
            switch (settings.BackgroundSettings)
            {
                case 0:
                    if ((double)Application.Current.Resources["PhoneDarkThemeOpacity"] == 1.0)
                    {
                        ThemeManager.ToDarkTheme();
                        break;
                    }
                    ThemeManager.ToLightTheme();
                    break;
                case 2:
                    ThemeManager.ToDarkTheme();
                    break;
                case 3:
                    ThemeManager.ToLightTheme();
                    break;
            }
            AppliedSettingsInfo.AppliedBGSetting = settings.BackgroundSettings;
            Logger.Instance.Info("App.ApplyThemeBasedOnSettings check 2");
            Logger.Instance.Info("App.ApplyThemeBasedOnSettings check 3");
        }

        private void InitializeServiceLocator()
        {
            ServiceLocator.Register<IAppStateInfo>((IAppStateInfo)this);
            ServiceLocator.Register<IVideoCatalogItemUCFactory>((IVideoCatalogItemUCFactory)new VideoCatalogItemUCFactory());
            ServiceLocator.Register<IConversationsUCFactory>((IConversationsUCFactory)new ConversationsUCFactory());
            ServiceLocator.Register<IBackendConfirmationHandler>((IBackendConfirmationHandler)new MessageBoxBackendConfirmationHandler());
            ServiceLocator.Register<IBackendNotEnoughMoneyHandler>((IBackendNotEnoughMoneyHandler)new BackendNotEnoughMoneyHandler());
            ServiceLocator.Register<IMediaPlayerWrapper>((IMediaPlayerWrapper)MediaPlayerWrapper.Instance);
            ServiceLocator.Register<IGZipEncoder>((IGZipEncoder)new GZipEncoder());
        }

        private void App_UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            AppendEarlyStartupMarker("UNHANDLED " + (e.ExceptionObject == null ? "null" : e.ExceptionObject.ToString()));
            Logger.Instance.ErrorAndSaveToIso("UNHANDLED", e.ExceptionObject);
            e.Handled = true;
            //this.ReportException(e.ExceptionObject);
        }

        private void Application_Launching(object sender, LaunchingEventArgs e)
        {
            AppendEarlyStartupMarker("09 launching entered");
            this.RemoveCurrentDeactivationSettings();
            AppendEarlyStartupMarker("10 deactivation settings removed");
            Logger.Instance.Info("App.Application_Launching check 1");
            this.PerformInitialization();
            AppendEarlyStartupMarker("11 PerformInitialization completed");
            Logger.Instance.Info("App.Application_Launching check 2");
            this.RestoreState(true);
            AppendEarlyStartupMarker("12 RestoreState completed");
            Logger.Instance.Info("App.Application_Launching check 3");
            PushNotificationsManager.Instance.Initialize();
            AppendEarlyStartupMarker("13 push initialized");
            Logger.Instance.Info("App.Application_Launching check 5");
            PlaylistManager.Initialize();
            AppendEarlyStartupMarker("14 playlist initialized");
            Logger.Instance.Info("App.Application_Launching check 6");
            //AudioCacheManager.Instance.ClearCache(null);
            //Logger.Instance.Info("App.Application_Launching check 7");
            BaseDataManager.Instance.NeedRefreshBaseData = true;
            App._uriMapper.NeedHandleActivation = true;
            AppendEarlyStartupMarker("15 base state initialized");
            ContactsManager.Instance.EnsureInSyncAsync(false);
            AppendEarlyStartupMarker("16 contacts ensure sync requested");
            this.RefreshPeopleHub();
            AppendEarlyStartupMarker("17 people hub refresh requested");
            ContactsSyncManager.Instance.Sync(null);
            AppendEarlyStartupMarker("18 contacts sync requested");
            ShareLaunchingEventArgs launchingEventArgs = e as ShareLaunchingEventArgs;
            if (launchingEventArgs == null)
                return;
            this.ShareOperation = launchingEventArgs.ShareTargetActivatedEventArgs.ShareOperation;
        }

        private void PerformInitialization()
        {
            MemoryInfo.Initialize();
            AppendEarlyStartupMarker("PI 01 memory");
            Navigator.Current = (INavigator)new NavigatorImpl();
            AppendEarlyStartupMarker("PI 02 navigator");
            Func<IPageDataRequesteeInfo> arg_2E_0 = new Func<IPageDataRequesteeInfo>(() => { return PageBase.CurrentPageDataRequestee; });

            JsonWebRequest.GetCurrentPageDataRequestee = arg_2E_0;
            AppendEarlyStartupMarker("PI 03 json web request");
            BirthdaysNotificationManager.Instance.Initialize();
            AppendEarlyStartupMarker("PI 04 birthdays");
            TileManager.Instance.Initialize();
            AppendEarlyStartupMarker("PI 05 tile manager");
            TileScheduledUpdate.Instance.Initialize();
            AppendEarlyStartupMarker("PI 06 tile scheduled update");
            TileManager.Instance.ResetContent();
            AppendEarlyStartupMarker("PI 07 tile reset");
            MessengerStateManagerInstance.Current = (IMessengerStateManager)new MessengerStateManager();
            AppendEarlyStartupMarker("PI 08 messenger state");
            SubscriptionFromPostManager.Instance.Restore();
            AppendEarlyStartupMarker("PI 09 subscriptions");
            MessengerStateManagerInstance.Current.AppStartedTime = DateTime.Now;
            AudioEventTranslator.Initialize();
            AppendEarlyStartupMarker("PI 10 audio translator");
            IsolatedStorageSettings.ApplicationSettings["ScaleFactor"] = Application.Current.Host.Content.ScaleFactor;
            AppendEarlyStartupMarker("PI 11 scale factor");
            BGAudioPlayerWrapper.InitializeInstance();
            AppendEarlyStartupMarker("PI 12 background audio");
            InstalledPackagesFinder.Instance.Initialize();
            AppendEarlyStartupMarker("PI 13 installed packages");
        }

        private void Application_Activated(object sender, ActivatedEventArgs e)
        {
            Logger.Instance.Info("App.Application_Activated check 1, InstancePreserved=" + e.IsApplicationInstancePreserved.ToString());
            if (!e.IsApplicationInstancePreserved)
                this.RestoreSessionType();
            if (e.IsApplicationInstancePreserved)
            {
                this.StartState = StartState.Reactivated;
                if (!ConversationsViewModel.IsInstanceNull)
                    ConversationsViewModel.Instance.NeedRefresh = true;
                NetworkStatusInfo.Instance.RetrieveNetworkStatus();
            }
            else
            {
                this.PerformInitialization();
                this.StartState = StartState.TombstonedThenRessurected;
                this.RestoreState(false);
            }
            Logger.Instance.Info("App.Application_Activated check 3");
            PushNotificationsManager.Instance.Initialize();
            Logger.Instance.Info("App.Application_Activated check 4");
            PlaylistManager.Initialize();
            Logger.Instance.Info("App.Application_Activated check 5");
            ContactsManager.Instance.EnsureInSyncAsync(false);
            this.RefreshPeopleHub();
            BaseDataManager.Instance.NeedRefreshBaseData = true;
            App._uriMapper.NeedHandleActivation = true;
        }

        private void Application_Deactivated(object sender, DeactivatedEventArgs e)
        {
            Logger.Instance.Info("App.Application_Deactivated check 1");
            this.SaveCurrentDeactivationSettings();
            this.RespondToDeactivationOrClose();
            Logger.Instance.Info("App.Application_Deactivated check 2");
            EventAggregator.Current.Publish(new ApplicationDeactivatedEvent());
        }

        private void Application_Closing(object sender, ClosingEventArgs e)
        {
            this.RemoveCurrentDeactivationSettings();
            this.RespondToDeactivationOrClose();
        }

        private void RespondToDeactivationOrClose()
        {
            AppGlobalStateManager.Current.GlobalState.LastDeactivatedTime = DateTime.Now;
            BGAudioPlayerWrapper.Instance.RespondToAppDeactivation();
            this.SaveState();
            TileManager.Instance.ResetContent();
        }

        private void RootFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            Logger.Instance.ErrorAndSaveToIso("RootFrame_NavigationFailed", e.Exception);
            if (!Debugger.IsAttached)
                return;
            Debugger.Break();
        }

        private void RestoreState(bool initialLaunch)
        {
            AppGlobalStateManager.Current.Initialize(true);
            CacheManager.TryDeserialize((IBinarySerializable)ImageCache.Current, App._imageDictionaryKey, CacheManager.DataType.CachedData);
            CountersManager.Current.Restore();
            AudioCacheManager.Instance.EnsureCachingInRunning();
            ConversationsViewModelUpdatesListener.Listen();
            ConversationViewModelCache.Current.SubscribeToUpdates();
            MediaLRUCache instance = MediaLRUCache.Instance;
            StickersAutoSuggestDictionary.Instance.RestoreStateAsync();
        }

        private void SaveState()
        {
            ConversationsViewModel.Save();
            CountersManager.Current.Save();
            AppGlobalStateManager.Current.SaveState();
            CacheManager.TrySerialize((IBinarySerializable)ImageCache.Current, App._imageDictionaryKey, false, CacheManager.DataType.CachedData);
            VeryLowProfileImageLoader.SaveState();
            SubscriptionFromPostManager.Instance.Save();
            ConversationViewModelCache.Current.FlushToPersistentStorage();
            AudioCacheManager.Instance.Save();
            MediaLRUCache.Instance.Save();
            StickersAutoSuggestDictionary.Instance.SaveState();
        }

        private void InitializePhoneApplication()
        {
            if (this.phoneApplicationInitialized)
                return;
            TransitionFrame transitionFrame = new TransitionFrame();
            SolidColorBrush solidColorBrush = new SolidColorBrush(Colors.Transparent);
            ((Control)transitionFrame).Background = ((Brush)solidColorBrush);
            this.RootFrame = (PhoneApplicationFrame)transitionFrame;
            this.RootFrame.Navigated += (new NavigatedEventHandler(this.CompleteInitializePhoneApplication));
            this.RootFrame.Navigated += (new NavigatedEventHandler(this.RootFrame_Navigated));
            this.RootFrame.NavigationStopped += (new NavigationStoppedEventHandler(this.RootFrame_NavigationStopped));
            App._uriMapper = new CustomUriMapper();
            this.RootFrame.UriMapper = ((UriMapperBase)App._uriMapper);
            this.RootFrame.NavigationFailed += (new NavigationFailedEventHandler(this.RootFrame_NavigationFailed));
            this.RootFrame.Navigating += (new NavigatingCancelEventHandler(this.RootFrame_Navigating));
            PhoneApplicationService.Current.ContractActivated += (new EventHandler<IActivatedEventArgs>(this.Application_ContractActivated));
            this.phoneApplicationInitialized = true;
        }

        private async void Application_ContractActivated(object sender, IActivatedEventArgs e)
        {
            FileSavePickerContinuationEventArgs saveArgs = e as FileSavePickerContinuationEventArgs;
            if (saveArgs != null)
            {
                object operation;
                if (saveArgs.File != null &&
                    saveArgs.ContinuationData.TryGetValue("Operation", out operation) &&
                    operation as string == "SaveDiagnosticsLog")
                {
                    await FileIO.WriteTextAsync(saveArgs.File, Logger.Instance.GetLog());
                }
                return;
            }

            FileOpenPickerContinuationEventArgs continuationEventArgs = e as FileOpenPickerContinuationEventArgs;
            if (continuationEventArgs == null)
                return;
            if (((IDictionary<string, object>)continuationEventArgs.ContinuationData).ContainsKey("FilePickedType"))
                ParametersRepository.SetParameterForId("FilePickedType", (AttachmentType)((IDictionary<string, object>)continuationEventArgs.ContinuationData)["FilePickedType"]);
            ParametersRepository.SetParameterForId("FilePicked", continuationEventArgs);
        }

        private void RootFrame_NavigationStopped(object sender, NavigationEventArgs e)
        {
            Logger.Instance.Info("App.RootFrame_NavigationStopped Mode={1}, Uri={0}", e.Uri.ToString(), e.NavigationMode);
        }

        private void RootFrame_Navigated(object sender, NavigationEventArgs e)
        {
            Logger.Instance.Info("App.RootFrame_Navigated Mode={1}, Uri={0}", e.Uri.ToString(), e.NavigationMode);
            if (e.NavigationMode == NavigationMode.Reset)
            {
                this.RootFrame.Navigated += (new NavigatedEventHandler(this.ClearBackStackAfterReset));
            }
            EventAggregator.Current.Publish(new RootFrameNavigatedEvent()
            {
                Uri = e.Uri
            });
        }

        private void RootFrame_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            Logger.Instance.Info("App.RootFrame_Navigating Mode={1}, Uri={0}", e.Uri.ToString(), e.NavigationMode);
            string str = e.Uri.ToString();
            if (str == "app://external/")
                return;
            if (!str.Contains("/LoginPage.xaml") && !str.Contains("/ValidatePage.xaml") && (!str.Contains("/WelcomePage.xaml") && !str.Contains("/RegistrationPage.xaml")) && (!str.Contains("/Auth2FAPage.xaml") && !str.Contains("/PhotoPickerPhotos.xaml")))
            {
                if (AppGlobalStateManager.Current.IsUserLoginRequired() && !this._handlingPreLoginNavigation)
                {
                    ((CancelEventArgs)e).Cancel = true;
                    this.RootFrame.Dispatcher.BeginInvoke(delegate
                      {
                          this.RootFrame.Navigate(new Uri("/WelcomePage.xaml", UriKind.Relative));
                      });
                }
                else if (str.Contains("TileLoggedInUserId") && str.Contains("IsChat=True"))
                {
                    int startIndex1 = str.IndexOf("TileLoggedInUserId");
                    int startIndex2 = str.IndexOf("=", startIndex1) + 1;
                    long result = 0;
                    if (long.TryParse(str.Substring(startIndex2), out result) && result != AppGlobalStateManager.Current.LoggedInUserId)
                    {
                        e.Cancel = true;
                        // ISSUE: method pointer
                        this.RootFrame.Dispatcher.BeginInvoke(delegate
                        {
                            this.RootFrame.Navigate(new Uri("/VKClient.Common;component/NewsPage.xaml", UriKind.Relative));
                        });
                    }
                }
            }
            this._handlingPreLoginNavigation = false;
            if (this.sessionType == App.SessionType.None && e.NavigationMode == NavigationMode.New)
            {
                if (this.IsDeepLink(e.Uri.ToString()))
                    this.sessionType = App.SessionType.DeepLink;
                else if (e.Uri.ToString().Contains("/NewsPage.xaml"))
                    this.sessionType = App.SessionType.Home;
            }
            if (e.NavigationMode == NavigationMode.Reset)
            {
                if ((e.Uri.OriginalString.Contains("RegistrationPage.xaml") || e.Uri.OriginalString.Contains("LoginPage.xaml") || e.Uri.OriginalString.Contains("Auth2FAPage.xaml")) && AppGlobalStateManager.Current.IsUserLoginRequired())
                    this._handlingPreLoginNavigation = true;
                this.wasRelaunched = true;
            }
            else
            {
                if (e.NavigationMode != NavigationMode.New || !this.wasRelaunched)
                    return;
                this.wasRelaunched = false;
                if (this.IsDeepLink(e.Uri.ToString()))
                {
                    this.sessionType = App.SessionType.DeepLink;
                }
                else
                {
                    if (!e.Uri.ToString().Contains("/NewsPage.xaml"))
                        return;
                    if (this.sessionType == App.SessionType.DeepLink)
                    {
                        this.sessionType = App.SessionType.Home;
                        e.Cancel = true;
                        this.RootFrame.Navigated -= (new NavigatedEventHandler(this.ClearBackStackAfterReset));
                    }
                    else
                    {
                        e.Cancel = true;
                        this.RootFrame.Navigated -= (new NavigatedEventHandler(this.ClearBackStackAfterReset));
                    }
                }
            }
        }

        private void CompleteInitializePhoneApplication(object sender, NavigationEventArgs e)
        {
            if (this.RootVisual != this.RootFrame)
                this.RootVisual = ((UIElement)this.RootFrame);
            
            this.RootFrame.Navigated -= (new NavigatedEventHandler(this.CompleteInitializePhoneApplication));
        }

        private void MenuItem_Click_1(object sender, RoutedEventArgs e)
        {
        }

        private void Grid_Tap_1(object sender, System.Windows.Input.GestureEventArgs e)
        {
        }

        private void TextBlock_Tap_1(object sender, System.Windows.Input.GestureEventArgs e)
        {
        }

        public bool AddOrUpdateValue(string Key, object value)
        {
            bool flag = false;
            try
            {
                if (this.Settings.Contains(Key))
                {
                    if (this.Settings[Key] != value)
                    {
                        this.Settings[Key] = value;
                        flag = true;
                    }
                }
                else
                {
                    this.Settings.Add(Key, value);
                    flag = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("App.AddOrUpdateValue failed.", ex);
            }
            return flag;
        }

        public void RemoveValue(string Key)
        {
            if (!this.Settings.Contains(Key))
                return;
            this.Settings.Remove(Key);
        }

        public void SaveCurrentDeactivationSettings()
        {
            if (!this.AddOrUpdateValue("SessionType", this.sessionType))
                return;
            this.Settings.Save();
        }

        public void RemoveCurrentDeactivationSettings()
        {
            this.RemoveValue("SessionType");
            this.Settings.Save();
        }

        private void RestoreSessionType()
        {
            if (!this.Settings.Contains("SessionType"))
                return;
            this.sessionType = (App.SessionType)this.Settings["SessionType"];
        }

        private void ClearBackStackAfterReset(object sender, NavigationEventArgs e)
        {
            this.RootFrame.Navigated -= (new NavigatedEventHandler(this.ClearBackStackAfterReset));
        }

        private bool IsDeepLink(string uri)
        {
            if (uri.Contains("ClearBackStack") || uri.Contains("/NewsPage.xaml") && uri.Contains("Action") || (uri.Contains("/NewsPage.xaml") && uri.Contains("uid=") || uri.Contains("/NewsPage.xaml") && uri.Contains("type=")) || (uri.Contains("/NewsPage.xaml") && uri.Contains("like_type=") || uri.Contains("/NewsPage.xaml") && uri.Contains("group_id=") || uri.Contains("/NewsPage.xaml") && uri.Contains("from_id=")))
                return true;
            if (uri.Contains("/NewsPage.xaml"))
                return uri.Contains("url=");
            return false;
        }
        
        public void ReportException(Exception exc)
        {/*
            if (exc == null)
                return;
            try
            {
                App.TelemetryClient.TrackException(exc);
            }
            catch (Exception )
            {
            }*/
        }
        
        public void HandleSuccessfulLogin(AutorizationData logInInfo, bool navigate = true)
        {
            Execute.ExecuteOnUIThread(delegate
            {
                AppGlobalStateManager.Current.HandleUserLogin(logInInfo);
                ConversationsViewModel.Instance = new ConversationsViewModel();
                ConversationsPage.ConversationsUCInstance = null;
                ContactsManager.Instance.EnsureInSyncAsync(false);
                this.RefreshPeopleHub();
                if (navigate)
                {
                    Navigator.Current.NavigateToMainPage();
                }
            });
        }

        private async void RefreshPeopleHub()
        {
            if (AppGlobalStateManager.Current.LoggedInUserId == 0L)
                return;
            await ContactsManager.Instance.EnsurePeopleHubProvisionedAsync();
            await SocialDataManager.Instance.ProcessSocialOperationsQueue();
        }

        private void ButtonTryAgain_OnTap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            e.Handled = true;
            ISupportReload dataContext = ((FrameworkElement)sender).DataContext as ISupportReload;
            if (dataContext == null)
                return;
            dataContext.Reload();
        }


        private enum SessionType
        {
            None,
            Home,
            DeepLink,
        }
    }
}
