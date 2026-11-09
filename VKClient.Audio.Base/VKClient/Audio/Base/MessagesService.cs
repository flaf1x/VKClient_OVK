using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using VKClient.Audio.Base.BLExtensions;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Library;
using VKClient.Common.Utils;
using VKMessenger.Backend;

namespace VKClient.Audio.Base
{
  public class MessagesService
  {
    private static MessagesService _instance;

    public static MessagesService Instance
    {
      get
      {
        if (MessagesService._instance == null)
          MessagesService._instance = new MessagesService();
        return MessagesService._instance;
      }
    }

    public void CreateChat(List<long> userIds, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["user_ids"] = userIds.Where(id => id > 0 && id != AppGlobalStateManager.Current.LoggedInUserId).Distinct().ToList().GetCommaSeparated();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("messages.createChat", parameters, callback, new Func<string, ResponseWithId>(JsonConvert.DeserializeObject<ResponseWithId>), false, true, new CancellationToken?(),  null);
    }

    public void SearchDialogs(string query, Action<BackendResult<List<object>, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["q"] = query;
      parameters["limit"] = "50";
      parameters["fields"] = "photo_max,photo_100,photo_200";
      VKRequestsDispatcher.DispatchRequestToVK<List<object>>("messages.searchDialogs", parameters, callback, (Func<string, List<object>>) (jsonStr =>
      {
        JArray results = JObject.Parse(jsonStr)["response"] as JArray ?? new JArray();
        List<object> objectList = new List<object>();
        foreach (JObject item in results.OfType<JObject>())
        {
          string type = (string)item["type"];
          if (type == "profile")
          {
            User user = item.ToObject<User>();
            UsersService.Instance.SetCachedUser(user);
            objectList.Add(user);
          }
          else if (type == "chat")
          {
            JToken users = item["users"];
            item.Remove("users");
            Chat chat = item.ToObject<Chat>();
            chat.users = new List<long>();
            if (users is JArray)
              foreach (JToken user in (JArray)users)
                chat.users.Add(user is JObject ? (long)(user["id"] ?? user["uid"] ?? 0) : (long)user);
            else if (users != null)
              foreach (string value in users.ToString().Split(','))
              {
                long id;
                if (long.TryParse(value.Trim(), out id))
                  chat.users.Add(id);
              }
            objectList.Add(chat);
          }
          else if (type == "group")
          {
            objectList.Add(new User() { id = -(long)item["id"], first_name = (string)item["name"], photo_max = (string)item["photo_200"] });
          }
        }
        return objectList;
      }), false, true, new CancellationToken?(),  null);
    }

    public void GetHistory(long userOrChatId, bool isChat, int offset, int count, int? startMessageId, Action<BackendResult<MessageListResponse, ResultCode>> callback)
    {
      if (startMessageId.HasValue && startMessageId.Value == -1)
      {
        // Modern OpenVK does not interpret start_message_id=-1 as "unread".
        // Find the window directly instead of walking backwards from latest.
        VKRequestsDispatcher.DispatchRequestToVK<HistoryAnchor>("messages.getConversationsById",
          new Dictionary<string, string>() { { "peer_ids", GetPeerId(userOrChatId, isChat).ToString() } },
          result =>
          {
            HistoryAnchor anchor = result.ResultCode == ResultCode.Succeeded ? result.ResultData : null;
            bool unread = anchor != null && anchor.UnreadCount > 0;
            GetHistoryPage(userOrChatId, isChat, unread && anchor.ReadId > 0 ? -13 : 0, count,
              unread && anchor.ReadId > 0 ? new int?(anchor.ReadId) : null,
              page =>
              {
                if (page.ResultCode == ResultCode.Succeeded && anchor != null)
                {
                  page.ResultData.LatestMessageId = anchor.LastId;
                  page.ResultData.Skipped = page.ResultData.Messages.Any(m => m.id >= anchor.LastId) ? 0 : 1;
                }
                callback(page);
              }, unread && anchor.ReadId <= 0);
          }, json =>
          {
            JObject response = JObject.Parse(json)["response"] as JObject;
            JArray items = response == null ? null : response["items"] as JArray;
            JObject item = items == null ? null : items.FirstOrDefault() as JObject;
            JObject conversation = item == null ? null : (item["conversation"] as JObject ?? item);
            return conversation == null ? new HistoryAnchor() : new HistoryAnchor {
              UnreadCount = (int?)conversation["unread_count"] ?? 0,
              ReadId = (int?)conversation["in_read"] ?? 0,
              LastId = (int?)conversation["last_message_id"] ?? 0
            };
          }, false, true, new CancellationToken?(), null);
        return;
      }
      GetHistoryPage(userOrChatId, isChat, offset, count, startMessageId, callback);
    }

    private class HistoryAnchor
    {
      public int UnreadCount;
      public int ReadId;
      public int LastId;
    }

    private void GetHistoryPage(long userOrChatId, bool isChat, int offset, int count, int? startMessageId, Action<BackendResult<MessageListResponse, ResultCode>> callback, bool oldestFirst = false)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["peer_id"] = this.GetPeerId(userOrChatId, isChat).ToString();
      if (startMessageId.HasValue && startMessageId.Value > 0)
        parameters["start_message_id"] = startMessageId.Value.ToString();
      parameters["offset"] = offset.ToString();
      parameters["count"] = count.ToString();
      if (oldestFirst)
        parameters["rev"] = "1";
      parameters["fields"] = "first_name_acc,last_name_acc,online,online_mobile,photo_max,sex,friend_status,photo_200,first_name_dat,is_messages_blocked";
      parameters["extended"] = "1";
      VKRequestsDispatcher.DispatchRequestToVK<MessageListResponse>("messages.getHistory", parameters, (Action<BackendResult<MessageListResponse, ResultCode>>) (res =>
      {
        if (res.ResultCode == ResultCode.Succeeded)
        {
          // OpenVK omits the old "skipped" field. For offset-based windows
          // the number of newer messages not loaded is precisely this offset.
          if (!startMessageId.HasValue || startMessageId.Value <= 0)
            res.ResultData.Skipped = Math.Max(0, offset);
          List<Message> messages = res.ResultData.Messages ?? new List<Message>();
          res.ResultData.Messages = messages;
          if (res.ResultData.Users == null)
            res.ResultData.Users = new List<User>();
          UsersService.Instance.SetCachedUsers((IEnumerable<User>) res.ResultData.Users);
          Logger.Instance.Info("OpenVK message history loaded: {0} items, {1} profiles", messages.Count, res.ResultData.Users.Count);
          int num = 1;
          List<long> list = Message.GetAssociatedUserIds(messages, num != 0).Select<long, long>((Func<long, long>) (uid => uid)).Distinct<long>().ToList<long>();
          List<long> missingUserIds = list
            .Where(uid => uid != 0L && uid > -2000000000L)
            .Except<long>(res.ResultData.Users.Select<User, long>((Func<User, long>) (u => u.uid)))
            .ToList<long>();
          if (missingUserIds.Count > 0)
            UsersService.Instance.GetUsers(missingUserIds, (Action<BackendResult<List<User>, ResultCode>>) (resUsers =>
            {
              if (resUsers.ResultCode == ResultCode.Succeeded)
                res.ResultData.Users.AddRange((IEnumerable<User>) (resUsers.ResultData ?? new List<User>()));
              else
                Logger.Instance.Info("Message history profile enrichment failed; rendering messages without it.");
              callback(res);
            }));
          else
            callback(res);
        }
        else
          callback(new BackendResult<MessageListResponse, ResultCode>(res.ResultCode));
      }), new Func<string, MessageListResponse>(this.GetMessageHistoryDataForJson), false, true, new CancellationToken?(),  null);
    }

    public long GetPeerId(long userOrChatId, bool isChat)
    {
      if (!isChat || userOrChatId >= 2000000000L)
        return userOrChatId;
      return userOrChatId + 2000000000L;
    }

    public static bool ShouldLoadOlderHistory(double position, double extent, double viewport, bool isUserScrolling)
    {
      return isUserScrolling && position > 150.0 && extent > 0.0 &&
        extent - viewport - position < 600.0;
    }

    // ObservableCollectionExtensions.AddOrdered uses >= 0, unlike List.Sort.
    // This comparator consequently stores real messages in ascending order.
    public static int CompareConversationMessages(Message first, Message second)
    {
      return first.mid > 0 && second.mid > 0 ? second.mid.CompareTo(first.mid) : second.date.CompareTo(first.date);
    }

    public static int FindUnreadDividerIndex(IList<Message> messages)
    {
      for (int i = 0; i < messages.Count; ++i)
        if (messages[i].mid > 0 && messages[i].@out == 0 && messages[i].read_state == 0) return i;
      return -1;
    }

    public void GetChat(long chatId, Action<BackendResult<ChatExtended, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<ChatExtended>("messages.getChat", new Dictionary<string, string>()
      {
        {
          "chat_id",
          chatId.ToString()
        },
        {
          "fields",
          "photo_200,photo_max,domain"
        }
      }, res => {
        if (res.ResultCode != ResultCode.Succeeded || res.ResultData == null) { callback(res); return; }
        AccountService.Instance.GetPeerPushSettings(chatId + 2000000000L, settings => {
          if (settings.ResultCode == ResultCode.Succeeded && settings.ResultData != null)
            res.ResultData.push_settings = settings.ResultData;
          callback(res);
        });
      },  null, false, true, new CancellationToken?(),  null);
    }

    public void GetDialogs(GetDialogsRequest request, Action<BackendResult<MessageListResponse, ResultCode>> callback)
    {
      var completed = callback;
      callback = result => EnrichDialogChats(result, completed);
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["offset"] = request.Offset.ToString();
      parameters["count"] = request.Count.ToString();
      parameters["preview_length"] = request.PreviewLength.ToString();
      parameters["fields"] = "first_name_acc,last_name_acc,online,online_mobile,photo_max,sex,photo_200,first_name_dat,is_messages_blocked";
      parameters["extended"] = "1";
      VKRequestsDispatcher.DispatchRequestToVK<MessageListResponse>("messages.getConversations", parameters, (Action<BackendResult<MessageListResponse, ResultCode>>) (res =>
      {
        if (res.ResultCode == ResultCode.Succeeded)
        {
          List<Message> list1 = res.ResultData.DialogHeaders.Select<DialogHeaderInfo, Message>((Func<DialogHeaderInfo, Message>) (d => d.message)).ToList<Message>();
          UsersService.Instance.SetCachedUsers((IEnumerable<User>) res.ResultData.Users);
          int num = 0;
          List<long> list2 = Message.GetAssociatedUserIds(list1, num != 0).Select<long, long>((Func<long, long>) (uid => uid)).Distinct<long>().ToList<long>();
          List<long> missingUserIds = list2
            .Where(uid => uid != 0L && uid > -2000000000L)
            .Except<long>(res.ResultData.Users.Select<User, long>((Func<User, long>) (u => u.uid)))
            .ToList<long>();
          if (missingUserIds.Count > 0)
            UsersService.Instance.GetUsers(missingUserIds, (Action<BackendResult<List<User>, ResultCode>>) (resUsers =>
            {
              if (resUsers.ResultCode == ResultCode.Succeeded)
              {
                res.ResultData.Users.AddRange((IEnumerable<User>) resUsers.ResultData);
                callback(res);
              }
              else
                callback(res);
            }));
          else
            callback(res);
        }
        else
          callback(new BackendResult<MessageListResponse, ResultCode>(res.ResultCode));
      }), new Func<string, MessageListResponse>(this.GetConversationsDataForJson), false, true, new CancellationToken?(),  null);
    }

    private MessageListResponse GetConversationsDataForJson(string jsonStr)
    {
      GenericRoot<ConversationsWrapper> root =
        JsonConvert.DeserializeObject<GenericRoot<ConversationsWrapper>>(jsonStr);
      ConversationsWrapper response = root != null ? root.response : null;
      if (response == null)
        response = new ConversationsWrapper();
      List<User> profiles = response.profiles ?? new List<User>();
      foreach (VKClient.Common.Backend.DataObjects.Group group in response.groups ?? new List<VKClient.Common.Backend.DataObjects.Group>())
      {
        User user = new User();
        user.InitFromGroup(group);
        profiles.Add(user);
      }

      List<DialogHeaderInfo> dialogs = new List<DialogHeaderInfo>();
      foreach (ConversationItem item in response.items ?? new List<ConversationItem>())
      {
        Message lastMessage = item.last_message ?? new Message();
        long peerId = item.conversation != null && item.conversation.peer != null
          ? item.conversation.peer.id
          : (long) lastMessage.user_id;
        // Current OpenVK responses identify a conversation through
        // conversation.peer even when last_message is absent or lacks the
        // legacy user_id/chat_id fields used by the WP client.
        if (peerId >= 2000000000L && peerId < 3000000000L)
        {
          if (lastMessage.chat_id == 0)
            lastMessage.chat_id = (int) (peerId - 2000000000L);
        }
        else if (peerId != 0L && peerId <= int.MaxValue)
        {
          lastMessage.user_id = (int) peerId;
        }
        int unread = item.conversation != null ? item.conversation.unread_count : 0;
        dialogs.Add(new DialogHeaderInfo()
        {
          unread = unread,
          message = lastMessage
        });
      }

      return new MessageListResponse()
      {
        DialogHeaders = dialogs,
        TotalCount = response.count,
        Users = profiles
      };
    }

    private void EnrichDialogChats(BackendResult<MessageListResponse, ResultCode> result,
      Action<BackendResult<MessageListResponse, ResultCode>> callback)
    {
      if (result.ResultCode != ResultCode.Succeeded || result.ResultData == null) { callback(result); return; }
      var messages = result.ResultData.DialogHeaders.Select(d => d.message).Where(m => m.chat_id != 0).ToList();
      if (messages.Count == 0) { callback(result); return; }
      // getConversations can omit the database-backed custom chat photo.
      // One batch per page, not a request for each row.
      VKRequestsDispatcher.DispatchRequestToVK<List<Chat>>("messages.getChat", new Dictionary<string, string> {
        { "chat_ids", string.Join(",", messages.Select(m => m.chat_id).Distinct()) }
      }, chats => {
        if (chats.ResultCode == ResultCode.Succeeded && chats.ResultData != null)
          foreach (var message in messages)
          {
            var chat = chats.ResultData.FirstOrDefault(c => c.id == message.chat_id);
            if (chat == null) continue;
            message.title = chat.title;
            message.photo_200 = chat.photo_200;
            message.chat_active = chat.users;
            message.HasChatMetadata = true;
          }
        callback(result); // Optional enrichment must not hide the dialog list.
      });
    }

    private sealed class ConversationsWrapper
    {
      public int count { get; set; }
      public List<ConversationItem> items { get; set; }
      public List<User> profiles { get; set; }
      public List<VKClient.Common.Backend.DataObjects.Group> groups { get; set; }
    }

    private sealed class ConversationItem
    {
      public ConversationInfo conversation { get; set; }
      public Message last_message { get; set; }
    }

    private sealed class ConversationInfo
    {
      public int unread_count { get; set; }
      public ConversationPeer peer { get; set; }
    }

    private sealed class ConversationPeer
    {
      public long id { get; set; }
    }

    public void GetMessages(List<long> messageIds, Action<BackendResult<MessageListResponse, ResultCode>> callback)
    {
      if (messageIds == null || messageIds.Count == 0)
      {
        callback(new BackendResult<MessageListResponse, ResultCode>(ResultCode.Succeeded, new MessageListResponse()
        {
          Messages = new List<Message>(),
          TotalCount = 0
        }));
      }
      else
      {
        Dictionary<string, string> parameters = new Dictionary<string, string>();
        parameters["message_ids"] = messageIds.GetCommaSeparated();
        parameters["fields"] = "first_name_acc,last_name_acc,online,online_mobile,photo_max,sex,friend_status,photo_200,is_messages_blocked";
        parameters["extended"] = "1";
        VKRequestsDispatcher.DispatchRequestToVK<MessageListResponse>("messages.getById", parameters, (Action<BackendResult<MessageListResponse, ResultCode>>) (res =>
        {
          if (res.ResultCode == ResultCode.Succeeded)
          {
            List<Message> messages = res.ResultData.Messages;
          UsersService.Instance.SetCachedUsers((IEnumerable<User>) res.ResultData.Users);
          int num = 1;
          List<long> list = Message.GetAssociatedUserIds(messages, num != 0).Select<long, long>((Func<long, long>) (uid => uid)).Distinct<long>().ToList<long>();
          List<long> missingUserIds = list
            .Where(uid => uid != 0L && uid > -2000000000L)
            .Except<long>(res.ResultData.Users.Select<User, long>((Func<User, long>) (u => u.uid)))
            .ToList<long>();
          if (missingUserIds.Count > 0)
            UsersService.Instance.GetUsers(missingUserIds, (Action<BackendResult<List<User>, ResultCode>>) (resUsers =>
              {
                if (resUsers.ResultCode == ResultCode.Succeeded)
                {
                  res.ResultData.Users.AddRange((IEnumerable<User>) resUsers.ResultData);
                  callback(res);
                }
                else
                  callback(res);
              }));
            else
              callback(res);
          }
          else
            callback(new BackendResult<MessageListResponse, ResultCode>(res.ResultCode));
        }), new Func<string, MessageListResponse>(this.GetMessageHistoryDataForJson), false, true, new CancellationToken?(),  null);
      }
    }

    private MessageListResponse GetDialogInfoDataForJson(string jsonStr)
    {
      DialogWrapper response = JsonConvert.DeserializeObject<GenericRoot<DialogWrapper>>(jsonStr).response;
      List<User> profiles = response.profiles ?? new List<User>();
      foreach (VKClient.Common.Backend.DataObjects.Group group in response.groups ?? new List<VKClient.Common.Backend.DataObjects.Group>())
      {
        User user = new User();
        user.InitFromGroup(group);
        profiles.Add(user);
      }
      return new MessageListResponse()
      {
        DialogHeaders = response.items,
        TotalCount = response.count,
        Users = profiles
      };
    }

    private MessageListResponse GetMessageHistoryDataForJson(string jsonStr)
    {
      MessageHistoryWrapper response = JsonConvert.DeserializeObject<GenericRoot<MessageHistoryWrapper>>(jsonStr).response;
      List<User> profiles = response.profiles ?? new List<User>();
      foreach (VKClient.Common.Backend.DataObjects.Group group in response.groups ?? new List<VKClient.Common.Backend.DataObjects.Group>())
      {
        User user = new User();
        user.InitFromGroup(group);
        profiles.Add(user);
      }
      return new MessageListResponse()
      {
        DialogHeaders = new List<DialogHeaderInfo>((response.items ?? new List<Message>()).Select<Message, DialogHeaderInfo>((Func<Message, DialogHeaderInfo>) (m => new DialogHeaderInfo()
        {
          message = m
        }))),
        Messages = response.items ?? new List<Message>(),
        Skipped = response.skipped,
        Unread = response.unread,
        TotalCount = response.count,
        Users = profiles
      };
    }

    private MessageListResponse GetMessageDataForJson(string jsonStr)
    {
      VKList<Message> response = JsonConvert.DeserializeObject<GenericRoot<VKList<Message>>>(jsonStr).response;
      return new MessageListResponse()
      {
        DialogHeaders = new List<DialogHeaderInfo>(response.items.Select<Message, DialogHeaderInfo>((Func<Message, DialogHeaderInfo>) (m => new DialogHeaderInfo()
        {
          message = m
        }))),
        Messages = response.items,
        TotalCount = response.count
      };
    }

    public void SendMessage(SendMessageRequest request, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      string index1 = "peer_id";
      string str1 = this.GetPeerId(request.UserOrCharId, request.IsChat).ToString();
      dictionary[index1] = str1;
      string index2 = "random_id";
      string str2 = (request.RandomId != 0 ? request.RandomId : NewRandomId()).ToString();
      dictionary[index2] = str2;
      string index3 = "message";
      string messageBody = request.MessageBody;
      dictionary[index3] = messageBody;
      Dictionary<string, string> parameters = dictionary;
      this.ProcessCommands(request.MessageBody ?? "");
      if (request.ReplyTo > 0)
        parameters["reply_to"] = request.ReplyTo.ToString(CultureInfo.InvariantCulture);
      if (request.AttachmentIds != null && request.AttachmentIds.Count > 0)
        parameters["attachment"] = request.AttachmentIds.GetCommaSeparated(",");
      if (request.ForwardedMessagesIds != null && request.ForwardedMessagesIds.Count > 0)
        parameters["forward_messages"] = request.ForwardedMessagesIds.GetCommaSeparated();
      if (request.StickerId != 0)
        parameters["sticker_id"] = request.StickerId.ToString();
      if (!string.IsNullOrEmpty(request.StickerReferrer))
        parameters["sticker_referrer"] = request.StickerReferrer;
      if (request.IsGeoAttached)
      {
        parameters["lat"] = request.Latitude.ToString((IFormatProvider) CultureInfo.InvariantCulture);
        parameters["long"] = request.Longitude.ToString((IFormatProvider) CultureInfo.InvariantCulture);
      }
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("messages.send", parameters, callback, new Func<string, ResponseWithId>(JsonConvert.DeserializeObject<ResponseWithId>), false, true, new CancellationToken?(),  null);
    }

    private void ProcessCommands(string command)
    {
      if (command == "%force_stats_enable%")
        AppGlobalStateManager.Current.GlobalState.ForceStatsSend = true;
      else if (command == "%force_stats_disable%")
        AppGlobalStateManager.Current.GlobalState.ForceStatsSend = false;
      else if (command == "%gif_enable%")
        AppGlobalStateManager.Current.GlobalState.GifAutoplayManualSetting = new bool?(true);
      else if (command == "%gif_disable%")
        AppGlobalStateManager.Current.GlobalState.GifAutoplayManualSetting = new bool?(false);
      else if (command == "%gif_default%")
        AppGlobalStateManager.Current.GlobalState.GifAutoplayManualSetting = new bool?();
      else if (command == "%ads_demo_enable%")
        AppGlobalStateManager.Current.GlobalState.AdsDemoManualSetting = new bool?(true);
      else if (command == "%ads_demo_disable%")
        AppGlobalStateManager.Current.GlobalState.AdsDemoManualSetting = new bool?(false);
      else if (command == "%ads_demo_default%")
        AppGlobalStateManager.Current.GlobalState.AdsDemoManualSetting = new bool?();
      else if (command.StartsWith("%set_domain="))
      {
        string str = command.Substring(12);
        if (!str.EndsWith("%"))
          return;
        AppGlobalStateManager.Current.GlobalState.BaseDomain = str.Substring(0, str.Length - 1);
      }
      else if (command.StartsWith("%set_login_domain="))
      {
        string str = command.Substring(18);
        if (!str.EndsWith("%"))
          return;
        AppGlobalStateManager.Current.GlobalState.BaseLoginDomain = str.Substring(0, str.Length - 1);
      }
      else if (command == "%reset_domain%")
      {
        AppGlobalStateManager.Current.GlobalState.BaseDomain = "";
        AppGlobalStateManager.Current.GlobalState.BaseLoginDomain = "";
      }
      else if (command == "%max_rec_demo%")
      {
        AppGlobalStateManager.Current.GlobalState.AudioRecordingMaxDemo = true;
      }
      else
      {
        if (!(command == "%max_rec_demo_reset%"))
          return;
        AppGlobalStateManager.Current.GlobalState.AudioRecordingMaxDemo = false;
      }
    }

    public void MarkAsRead(List<long> messageIds, long peerId, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      if (messageIds.IsNullOrEmpty() || peerId == 0L)
      {
        callback(new BackendResult<ResponseWithId, ResultCode>(ResultCode.Succeeded, new ResponseWithId() { response = 1 }));
        return;
      }
      long newestReadId = messageIds.Max();
      var parameters = new Dictionary<string, string>();
      parameters["peer_id"] = peerId.ToString();
      parameters["start_message_id"] = newestReadId.ToString();
      parameters["message_ids"] = messageIds.GetCommaSeparated();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("messages.markAsRead", parameters, callback,
        JsonConvert.DeserializeObject<ResponseWithId>);
    }

    public void GetConversationUnreadCount(long peerId, Action<BackendResult<int, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<int>("messages.getConversationsById",
        new Dictionary<string, string> { { "peer_ids", peerId.ToString() } }, callback, json => {
          JObject response = JObject.Parse(json)["response"] as JObject;
          JArray items = response == null ? null : response["items"] as JArray;
          JObject item = items == null ? null : items.FirstOrDefault() as JObject;
          JObject conversation = item == null ? null : item["conversation"] as JObject ?? item;
          if (conversation == null || conversation["unread_count"] == null) throw new InvalidDataException("Missing unread count");
          return Math.Max(0, (int)conversation["unread_count"]);
        });
    }

    public static int NewRandomId()
    {
      int id = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) & int.MaxValue;
      return id == 0 ? 1 : id;
    }

    public void SetUserIsTyping(long userOrChatId, bool isChatId, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["peer_id"] = this.GetPeerId(userOrChatId, isChatId).ToString();
      parameters["type"] = "typing";
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("messages.setActivity", parameters, callback, new Func<string, ResponseWithId>(JsonConvert.DeserializeObject<ResponseWithId>), false, true, new CancellationToken?(),  null);
    }

    public void DeleteMessages(List<int> messageIds, Action<BackendResult<ResponseWithId, ResultCode>> callback, long peerId = 0)
    {
      this.SetStatusByIds(messageIds, "messages.delete", callback, peerId);
    }

    private void SetStatusByIds(List<int> messageIds, string methodName, Action<BackendResult<ResponseWithId, ResultCode>> callback, long peerId = 0)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters.Add("message_ids", messageIds.GetCommaSeparated());
      if (peerId != 0)
        parameters["peer_id"] = peerId.ToString();
      if (messageIds.Count == 0)
        callback(new BackendResult<ResponseWithId, ResultCode>(ResultCode.Succeeded, new ResponseWithId()));
      else
        VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>(methodName, parameters, callback, json =>
        {
          JToken result = JObject.Parse(json)["response"];
          bool succeeded = result != null && (result.Type == JTokenType.Object
            ? messageIds.All(id => (int?)result[id.ToString()] == 1)
            : (int?)result == 1);
          if (!succeeded)
            throw new InvalidOperationException("Messages were not deleted.");
          return new ResponseWithId() { response = 1 };
        }, false, true, new CancellationToken?(), null);
    }

    public void GetPhotoUploadServer(Action<BackendResult<UploadServerAddress, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<UploadServerAddress>("photos.getMessagesUploadServer", new Dictionary<string, string>(), callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void SavePhoto(UploadResponseData uploadData, Action<BackendResult<Photo, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary["server"] = uploadData.server;
      dictionary["photo"] = uploadData.photo;
      dictionary["hash"] = uploadData.hash;
      string methodName = "photos.saveMessagesPhoto";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<Photo, ResultCode>> callback1 = callback;
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<Photo>(methodName, parameters, callback1, (Func<string, Photo>) (jsonStr => JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<List<Photo>>>(jsonStr).response.First<Photo>()), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void SearchMessages(string query, int count, int offset, Action<BackendResult<MessageListResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["q"] = query;
      parameters["offset"] = offset.ToString();
      parameters["preview_length"] = "0";
      parameters["count"] = count.ToString();
      parameters["extended"] = "1";
      parameters["fields"] = "photo_200,photo_max,online,sex";
      VKRequestsDispatcher.DispatchRequestToVK<MessageListResponse>("messages.search", parameters, callback,
        this.GetMessageHistoryDataForJson, false, true, new CancellationToken?(), null);
    }

    public void UploadPhoto(byte[] photoData, Action<BackendResult<Photo, ResultCode>> callback, Action<double> progressCallback = null, Cancellation c = null)
    {
      this.GetPhotoUploadServer((Action<BackendResult<UploadServerAddress, ResultCode>>) (getUplResp =>
      {
        if (getUplResp.ResultCode != ResultCode.Succeeded)
          callback(new BackendResult<Photo, ResultCode>(getUplResp.ResultCode));
        else
          JsonWebRequest.Upload(getUplResp.ResultData.upload_url, (Stream) new MemoryStream(photoData), "photo", "image", (Action<JsonResponseData>) (jsonResult =>
          {
            if (!jsonResult.IsSucceeded)
              callback(new BackendResult<Photo, ResultCode>(ResultCode.UnknownError));
            else
              this.SavePhoto(JsonConvert.DeserializeObject<UploadResponseData>(jsonResult.JsonString), callback);
          }), "MyImage.jpg", progressCallback, c);
      }));
    }

    public void GetVideo(long ownerId, long videoId, Action<BackendResult<VideoData, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["videos"] = ownerId.ToString() + "_" + videoId.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<VideoData>("video.get", parameters, callback, (Func<string, VideoData>) (jsonStr =>
      {
        JToken response = JObject.Parse(jsonStr)["response"];
        JArray items = response as JArray ?? (response as JObject)?["items"] as JArray;
        JObject video = items == null ? null : items.OfType<JObject>().FirstOrDefault();
        return video == null ? new VideoData() : video.ToObject<VideoData>();
      }), false, true, new CancellationToken?(),  null);
    }

    public void GetAudio(long ownerId, long audioId, Action<BackendResult<AudioObj, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary["audios"] = ownerId.ToString() + "_" + audioId.ToString();
      string methodName = "audio.getById";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<AudioObj, ResultCode>> callback1 = callback;
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<AudioObj>(methodName, parameters, callback1, (Func<string, AudioObj>) (jsonStr => (JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<VKList<AudioObj>>>(jsonStr).response.items ?? new List<AudioObj>()).FirstOrDefault<AudioObj>()), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void DeleteDialog(long userOrChatId, bool isChat, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["peer_id"] = this.GetPeerId(userOrChatId, isChat).ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("messages.deleteDialog", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    private string RemoveCountOfRecords(string jsonStr)
    {
      int num = jsonStr.IndexOf(",");
      if (num == -1)
        return jsonStr;
      return jsonStr.Remove(13, num - 13 + 1);
    }

    public void GetChatUploadServer(long chatId, Action<BackendResult<UploadServerAddress, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["chat_id"] = chatId.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<UploadServerAddress>("photos.getChatUploadServer", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void SetChatPhoto(long chatId, UploadResponseData uploadData, Action<BackendResult<ChatInfoWithMessageId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["chat_id"] = chatId.ToString();
      parameters["file"] = uploadData.photo;
      parameters["hash"] = uploadData.hash;
      VKRequestsDispatcher.DispatchRequestToVK<ChatInfoWithMessageId>("messages.setChatPhoto", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void UpdateChatPhoto(long chatId, byte[] photoData, Rect thumbnailRect, Action<BackendResult<ChatInfoWithMessageId, ResultCode>> callback)
    {
      this.GetChatUploadServer(chatId, (Action<BackendResult<UploadServerAddress, ResultCode>>) (getUplResp =>
      {
        if (getUplResp.ResultCode != ResultCode.Succeeded)
        {
          callback(new BackendResult<ChatInfoWithMessageId, ResultCode>(getUplResp.ResultCode));
        }
        else
        {
          string uploadUrl = getUplResp.ResultData.upload_url;
          // OpenVK consumes the already-cropped JPEG. Keep its signed URL
          // intact instead of appending legacy VK crop query parameters.
          MemoryStream memoryStream = new MemoryStream(photoData);
          JsonWebRequest.Upload(uploadUrl, (Stream) memoryStream, "photo", "image/jpeg", (Action<JsonResponseData>) (jsonResult =>
          {
            memoryStream.Dispose();
            if (!jsonResult.IsSucceeded)
              callback(new BackendResult<ChatInfoWithMessageId, ResultCode>(ResultCode.UnknownError));
            else
            {
              try
              {
                var uploaded = JsonConvert.DeserializeObject<UploadResponseData>(jsonResult.JsonString);
                if (uploaded == null || string.IsNullOrEmpty(uploaded.photo) || string.IsNullOrEmpty(uploaded.hash))
                  callback(new BackendResult<ChatInfoWithMessageId, ResultCode>(ResultCode.UploadingFailed));
                else this.SetChatPhoto(chatId, uploaded, callback);
              }
              catch (JsonException) { callback(new BackendResult<ChatInfoWithMessageId, ResultCode>(ResultCode.UploadingFailed)); }
            }
          }), "MyImage.jpg",  null,  null);
        }
      }));
    }

    public void DeleteChatPhoto(long chatId, Action<BackendResult<ChatInfoWithMessageId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["chat_id"] = chatId.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ChatInfoWithMessageId>("messages.deleteChatPhoto", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetConversationMaterials(long peerId, string mediaType, string startFrom, int count, Action<BackendResult<VKList<ConversationMaterial>, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        {
          "peer_id",
          peerId.ToString()
        },
        {
          "media_type",
          mediaType
        },
        {
          "count",
          count.ToString()
        }
      };
      if (startFrom != null)
        parameters.Add("start_from", startFrom);
      VKRequestsDispatcher.DispatchRequestToVK<VKList<ConversationMaterial>>("messages.getHistoryAttachments", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void AllowDenyMessagesFromGroup(long groupId, bool allow, Action<BackendResult<int, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      string index = "group_id";
      string str = groupId.ToString();
      dictionary[index] = str;
      Dictionary<string, string> parameters = dictionary;
      VKRequestsDispatcher.DispatchRequestToVK<int>(allow ? "messages.allowMessagesFromGroup" : "messages.denyMessagesFromGroup", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public class RootObject
    {
      public List<Message> response { get; set; }
    }
  }
}
