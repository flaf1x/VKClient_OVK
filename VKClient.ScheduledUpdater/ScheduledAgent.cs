using Microsoft.Phone.Scheduler;
using Microsoft.Phone.Shell;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.IsolatedStorage;
using System.Windows;
using VKClient.Audio.Base;
using VKClient.Audio.Base.Social;
using VKClient.Audio.Base.Utils;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Library;
using VKClient.Common.Utils;

namespace VKClient.ScheduledUpdater
{
    public class ScheduledAgent : ScheduledTaskAgent
    {
        private static volatile bool _classInitialized;

        public ScheduledAgent()
        {
            if (!ScheduledAgent._classInitialized)
            {
                ScheduledAgent._classInitialized = true;
                Deployment.Current.Dispatcher.BeginInvoke(delegate
                {
                    Application.Current.UnhandledException+=(new EventHandler<ApplicationUnhandledExceptionEventArgs>(this.ScheduledAgent_UnhandledException));
                });
            }
        }

        private void ScheduledAgent_UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            Logger.Instance.Error("UNHANDLED exception in ScheduledAgent" + e.ExceptionObject);
            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
        }

        protected override async void OnInvoke(ScheduledTask task)
        {
            Logger.Instance.Info(("Entering ScheduledAgent.OnInvoke, task.Name=" + task.Name) ?? "", new object[0]);
            try
            {
                AppGlobalStateManager.Current.Initialize(true);
                if (AppGlobalStateManager.Current.LoggedInUserId != 0L)
                {
                    if (task.Name == "ExtensibilityTaskAgent")
                    {
                        await SocialDataManager.Instance.ProcessSocialOperationsQueue();
                        this.NotifyComplete();
                    }
                    else
                    {
                        OpenVKNotificationsManager.CheckForUpdates(true, (Action) delegate
                        {
                            this.UpdateTilesAndComplete();
                        });
                    }
                }
                else
                {
                    this.NotifyComplete();
                }
            }
            catch (Exception var_2_FE)
            {
                Logger.Instance.Error("ScheduledAgent.OnInvoke failed", var_2_FE);
                this.NotifyComplete();
            }
        }

        private void UpdateTilesAndComplete()
        {
            try
            {
                        SecondaryTileManager.Instance.UpdateAllExistingTiles(delegate(bool resSecondary)
                        {
                            CountersService.Instance.GetCountersWithLastMessage(delegate(BackendResult<CountersWithMessageInfo, ResultCode> res)
                            {
                                if (res.ResultCode == ResultCode.Succeeded)
                                {
                                    string content = "";
                                    string content2 = "";
                                    string content3 = "";
                                    if (res.ResultData.LastMessage != null && BaseFormatterHelper.UnixTimeStampToDateTime((double)res.ResultData.LastMessage.date, true) > AppGlobalStateManager.Current.GlobalState.LastDeactivatedTime)
                                    {
                                        MessageHeaderFormatterHelper.FormatForTileIntoThreeStrings(res.ResultData.LastMessage, res.ResultData.User, out content, out content2, out content3);
                                        this.ShowMessageToastIfNeeded(res.ResultData, content, content2);
                                    }
                                    int messages = res.ResultData.Counters.messages;
                                    TileManager.Instance.SetContentAndCount(content, content2, content3, messages, delegate
                                    {
                                        base.NotifyComplete();
                                    });
                                    return;
                                }
                                base.NotifyComplete();
                            });
                        });
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("ScheduledAgent tile update failed", ex);
                this.NotifyComplete();
            }
        }

        private void ShowMessageToastIfNeeded(CountersWithMessageInfo data, string title, string text)
        {
            if (data == null || data.LastMessage == null ||
                data.LastMessage.@out != 0 ||
                !AppGlobalStateManager.Current.GlobalState.PushNotificationsEnabled ||
                AppGlobalStateManager.Current.GlobalState.PushNotificationsBlockedUntil >= DateTime.UtcNow)
                return;

            bool isChat = data.LastMessage.chat_id != 0;
            PushSettings settings = AppGlobalStateManager.Current.GlobalState.PushSettings;
            if (isChat && !settings.chat || !isChat && !settings.msg)
                return;

            const string key = "OpenVK.Messages.LastToastDate";
            int lastDate = 0;
            object stored;
            if (IsolatedStorageSettings.ApplicationSettings.TryGetValue(key, out stored) &&
                stored is int)
                lastDate = (int)stored;
            if (data.LastMessage.date <= lastDate)
                return;

            IsolatedStorageSettings.ApplicationSettings[key] = data.LastMessage.date;
            IsolatedStorageSettings.ApplicationSettings.Save();
            if (isChat ? settings.chat_no_text : settings.msg_no_text)
                text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
                    ? "Новое сообщение"
                    : "New message";

            long id = isChat ? data.LastMessage.chat_id : data.LastMessage.user_id;
            ShellToast toast = new ShellToast();
            toast.Title = string.IsNullOrWhiteSpace(title) ? "OpenVK" : title;
            toast.Content = string.IsNullOrWhiteSpace(text)
                ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
                    ? "Новое сообщение"
                    : "New message")
                : text;
            toast.NavigationUri = new Uri(string.Format(
                "/VKMessenger;component/Views/ConversationPage.xaml?UserOrChatId={0}&IsChat={1}&FromLookup=False&NewMessageContents=False&MessageId=0&IsContactProductSellerMode=False",
                id, isChat), UriKind.Relative);
            toast.Show();
        }
    }
}
