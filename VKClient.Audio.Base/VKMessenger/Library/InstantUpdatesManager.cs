using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using VKClient.Audio.Base;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Library;
using VKClient.Common.Utils;
using VKMessenger.Backend;

namespace VKMessenger.Library
{
  public class InstantUpdatesManager
  {
    private DelayedExecutor _de = new DelayedExecutor(1000);
    private static InstantUpdatesManager _current;
    private int _generation;

    public static InstantUpdatesManager Current
    {
      get
      {
        if (InstantUpdatesManager._current == null)
          InstantUpdatesManager._current = new InstantUpdatesManager();
        return InstantUpdatesManager._current;
      }
    }

    public event InstantUpdatesManager.UpdatesReceivedEventHandler ReceivedUpdates;

    public void Restart()
    {
      int generation = Interlocked.Increment(ref this._generation);
      Logger.Instance.Info("InstantUpdatesManager.Restart");
      if (!BackendServices.LongPollServerService.HaveToken())
      {
        Logger.Instance.Info("InstantUpdatesManager.Restart: no token is currently available");
      }
      else
      {
        string t = VKRequestsDispatcher.Token;
        BackendServices.LongPollServerService.GetLongPollServer((Action<BackendResult<LongPollServerResponse, ResultCode>>) (result =>
        {
          if (generation != this._generation || t != VKRequestsDispatcher.Token)
            return;
          Logger.Instance.Info("InstantUpdatesManager.Restart callback result = {0}", result.ResultCode);
          if (result.ResultCode == ResultCode.Succeeded)
            this.RunRequestsLoop(t, result.ResultData, generation);
          else
            this._de.AddToDelayedExecution(() => { if (generation == this._generation) this.Restart(); });
        }));
      }
    }

    private void RunRequestsLoop(string token, LongPollServerResponse requestsSettings, int generation)
    {
      DateTime requestStarted = DateTime.UtcNow;
      try
      {
        LongPollServerService.Instance.GetUpdates(requestsSettings.server, requestsSettings.key, requestsSettings.ts, (Action<BackendResult<UpdatesResponse, LongPollResultCode>>) (res =>
        {
          Logger.Instance.Info("InstantUpdatesManager.RunRequestsLoop callback result = {0}", res.ResultCode);
          if (VKRequestsDispatcher.Token != token || generation != this._generation)
            return;
          if (res.ResultCode == LongPollResultCode.RequireNewPollServer)
            this.Restart();
          if (res.ResultCode == LongPollResultCode.Succeeded)
          {
            this.LogReceivedUpdates(res.ResultData);
            AppGlobalStateManager.Current.GlobalState.LastTS = res.ResultData.ts;
            requestsSettings.ts = res.ResultData.ts;
            this.ProcessReceivedData(res.ResultData.Updates, () =>
            {
              Action next = () =>
              {
                if (generation == this._generation && VKRequestsDispatcher.Token == token)
                  this.RunRequestsLoop(token, requestsSettings, generation);
              };
              if (res.ResultData.Updates.Count == 0 && (DateTime.UtcNow - requestStarted).TotalMilliseconds < 1000)
                this._de.AddToDelayedExecution(next);
              else
                next();
            });
          }
          if (res.ResultCode != LongPollResultCode.CommunicationFailed)
            return;
          this._de.AddToDelayedExecution(() => { if (generation == this._generation) this.Restart(); });
        }));
      }
      catch (Exception ex)
      {
        Logger.Instance.Error("RunRequestLoop failed", ex);
        this.Restart();
      }
    }

    private void ProcessReceivedData(List<LongPollServerUpdateData> updateData, Action completed)
    {
      this.EnrichUpdateData(updateData, (Action) (() =>
      {
        try
        {
          foreach (var update in updateData.Where(u => u.UpdateType == LongPollServerUpdateType.NotificationSettingsChanged && u.isChat))
          {
            var settings = new VKMessenger.Backend.PushSettings { sound = update.NotificationSound, disabled_until = update.NotificationsDisabledUntil };
            VKClient.Common.Framework.Execute.ExecuteOnUIThread(() => VKClient.Common.Framework.EventAggregator.Current.Publish(
              new VKClient.Audio.Base.Events.NotificationSettingsChangedEvent {
                ChatId = update.chat_id, AreNotificationsDisabled = settings.AreDisabledNow, PushSettings = settings }));
          }
          // ISSUE: reference to a compiler-generated field
          InstantUpdatesManager.UpdatesReceivedEventHandler receivedUpdates = this.ReceivedUpdates;
          if (receivedUpdates == null)
            return;
          List<LongPollServerUpdateData> updates = updateData;
          receivedUpdates(updates);
        }
        catch (Exception ex)
        {
          Logger.Instance.Error("InstantUpdatesManager ReceivedUpdates", ex);
        }
        finally
        {
          completed();
        }
      }));
    }

    private void EnrichUpdateData(List<LongPollServerUpdateData> updateData, Action callback)
    {
      List<long> list = updateData.Where<LongPollServerUpdateData>((Func<LongPollServerUpdateData, bool>) (u =>
      {
        // OpenVK long poll deliberately carries only a compact event. Always
        // enrich new/restored messages through messages.getById so text and
        // attachments use the same model as the working history endpoint.
        return u.UpdateType == LongPollServerUpdateType.MessageHasBeenAdded ||
               u.UpdateType == LongPollServerUpdateType.MessageHasBeenRestored ||
               u.UpdateType == LongPollServerUpdateType.MessageHasBeenEdited;
      })).Select<LongPollServerUpdateData, long>((Func<LongPollServerUpdateData, long>) (u => u.message_id)).ToList<long>();
      if (list.Count == 0)
      {
        callback();
        return;
      }
      List<int> associatedUsersIds = updateData.Where<LongPollServerUpdateData>((Func<LongPollServerUpdateData, bool>) (u =>
      {
        if (u.UpdateType != LongPollServerUpdateType.MessageHasBeenAdded)
          return u.UpdateType == LongPollServerUpdateType.MessageHasBeenRestored;
        return true;
      })).Select<LongPollServerUpdateData, int>((Func<LongPollServerUpdateData, int>) (u => (int) u.user_id)).Distinct<int>().ToList<int>();
      BackendServices.MessagesService.GetMessages(list, (Action<BackendResult<MessageListResponse, ResultCode>>) (resMessages =>
      {
        if (resMessages.ResultCode != ResultCode.Succeeded)
        {
          foreach (LongPollServerUpdateData update in updateData.Where(u => u.UpdateType == LongPollServerUpdateType.MessageHasBeenAdded))
          {
            update.user = new User() { uid = update.user_id };
            update.message = new Message() { id = (int)update.message_id, user_id = (int)update.user_id,
              chat_id = (int)update.chat_id, body = update.text, date = (int)update.timestamp, @out = update.@out ? 1 : 0 };
          }
          callback();
          return;
        }
        associatedUsersIds.AddRange((IEnumerable<int>) Message.GetAssociatedUserIds(resMessages.ResultData.Messages, true).Select<long, int>((Func<long, int>) (l => (int) l)).ToList<int>());
        associatedUsersIds = associatedUsersIds.Where(id => id != 0 && id < 2000000000).Distinct<int>().ToList<int>();
        UsersService.Instance.GetUsers(associatedUsersIds.Select<int, long>((Func<int, long>) (uid => (long) uid)).ToList<long>(), (Action<BackendResult<List<User>, ResultCode>>) (resUsers =>
        {
          List<User> users = resUsers.ResultCode == ResultCode.Succeeded
            ? resUsers.ResultData : (resMessages.ResultData.Users ?? new List<User>());
          updateData.ForEach((Action<LongPollServerUpdateData>) (d =>
          {
            if (d.UpdateType != LongPollServerUpdateType.MessageHasBeenAdded &&
                d.UpdateType != LongPollServerUpdateType.MessageHasBeenRestored &&
                d.UpdateType != LongPollServerUpdateType.MessageHasBeenEdited)
              return;
            Message message = resMessages.ResultData.Messages.FirstOrDefault<Message>((Func<Message, bool>) (m => (long) m.mid == d.message_id));
            if (message != null && (d.user_id == 0L || d.isChat))
              d.user_id = (long) message.uid;
            d.user = users.FirstOrDefault<User>((Func<User, bool>) (u => u.uid == d.user_id)) ?? new User() { uid = d.user_id };
            if (d.message == null)
            {
              if (message == null)
                d.message = new Message()
                {
                  uid = (int) d.user_id,
                  body = d.text,
                  read_state = 0,
                  mid = (int) d.message_id,
                  date = (int) d.timestamp,
                  @out = d.@out ? 1 : 0,
                  chat_id = (int) d.chat_id
                };
              else
                d.message = message;
            }
            AppGlobalStateManager.Current.GlobalState.MaxMessageId = Math.Max(AppGlobalStateManager.Current.GlobalState.MaxMessageId, d.message_id);
          }));
          callback();
        }));
      }));
    }

    private void LogReceivedUpdates(UpdatesResponse updatesResponse)
    {
      StringBuilder stringBuilder = new StringBuilder("Received the following updates:" + Environment.NewLine);
      foreach (LongPollServerUpdateData update in updatesResponse.Updates)
        stringBuilder = stringBuilder.Append(update.ToString() + Environment.NewLine);
    }

    public delegate void UpdatesReceivedEventHandler(List<LongPollServerUpdateData> updates);
  }
}
