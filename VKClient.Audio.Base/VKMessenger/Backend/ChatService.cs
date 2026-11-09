using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using Chat = VKClient.Audio.Base.DataObjects.Chat;

namespace VKMessenger.Backend
{
  public class ChatService : IChatService
  {
    public void GetChatInfo(long chatId, Action<BackendResult<ChatInfo, ResultCode>> callback)
    {
      // OpenVK's execute does not support the legacy array projection used here.
      var parameters = new Dictionary<string, string> { { "chat_id", chatId.ToString() } };
      VKRequestsDispatcher.DispatchRequestToVK<Chat>("messages.getChat", parameters, chatResult =>
      {
        if (chatResult.ResultCode != ResultCode.Succeeded || chatResult.ResultData == null)
        {
          callback(new BackendResult<ChatInfo, ResultCode>(chatResult.ResultCode == ResultCode.Succeeded ? ResultCode.UnknownError : chatResult.ResultCode));
          return;
        }
        var memberParameters = new Dictionary<string, string> {
          { "peer_id", (chatId + 2000000000L).ToString() }, { "extended", "1" }
        };
        VKRequestsDispatcher.DispatchRequestToVK<ChatMembersResponse>("messages.getConversationMembers", memberParameters, memberResult =>
        {
          if (memberResult.ResultCode != ResultCode.Succeeded)
          {
            callback(new BackendResult<ChatInfo, ResultCode>(memberResult.ResultCode));
            return;
          }
          var members = memberResult.ResultData ?? new ChatMembersResponse();
          var profiles = members.profiles ?? new List<User>();
          var groups = members.groups ?? new List<Group>();
          var info = new ChatInfo { chat = chatResult.ResultData,
            chat_participants = new List<ChatUser>(), invited_by_users = profiles.ToList() };
          foreach (var record in members.items ?? new List<ChatMemberRecord>())
          {
            User profile = profiles.FirstOrDefault(u => u.id == record.member_id);
            Group group = groups.FirstOrDefault(g => -g.id == record.member_id);
            ChatUser user = profile != null ? new ChatUser(profile, record.invited_by) : new ChatUser {
              id = record.member_id, invited_by = record.invited_by,
              first_name = group != null ? group.name : "Участник", photo_200 = group != null ? group.photo_200 : null
            };
            user.can_kick = record.can_kick;
            info.chat_participants.Add(user);
          }
          var inviters = info.chat_participants.Select(m => m.invited_by)
            .Where(id => id > 0 && !profiles.Any(p => p.id == id)).Distinct().ToList();
          if (inviters.Count == 0)
          {
            callback(new BackendResult<ChatInfo, ResultCode>(ResultCode.Succeeded, info));
            return;
          }
          VKRequestsDispatcher.DispatchRequestToVK<List<User>>("users.get", new Dictionary<string, string> {
            { "user_ids", string.Join(",", inviters) }, { "fields", "sex" }
          }, usersResult => {
            info.invited_by_users.AddRange(usersResult.ResultData ?? new List<User>());
            callback(new BackendResult<ChatInfo, ResultCode>(ResultCode.Succeeded, info));
          });
        });
      });
    }

    public void EditChat(long chatId, string title, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["chat_id"] = chatId.ToString();
      parameters["title"] = title;
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("messages.editChat", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void RemoveChatUsers(long chatId, List<long> usersToBeRemoved, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      ChangeMembers(chatId, usersToBeRemoved.Distinct().ToList(), 0, "messages.removeChatUser", callback);
    }

    private void RemoveChatUsersOneByOne(long chatId, List<long> usersToBeRemoved, int ind, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      if (ind >= usersToBeRemoved.Count)
        return;
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["chat_id"] = chatId.ToString();
      parameters["user_id"] = usersToBeRemoved[ind].ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("messages.removeChatUser", parameters, (Action<BackendResult<ResponseWithId, ResultCode>>) (res =>
      {
        if (res.ResultCode == ResultCode.Succeeded && ind + 1 < usersToBeRemoved.Count)
          this.RemoveChatUsersOneByOne(chatId, usersToBeRemoved, ind + 1, callback);
        else
          callback(res);
      }), (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void AddChatUsers(long chatId, List<long> uids, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      ChangeMembers(chatId, uids.Distinct().ToList(), 0, "messages.addChatUser", callback);
    }

    private void ChangeMembers(long chatId, List<long> users, int index, string method,
      Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      if (index >= users.Count)
      {
        callback(new BackendResult<ResponseWithId, ResultCode>(ResultCode.Succeeded, new ResponseWithId() { response = 1 }));
        return;
      }
      var parameters = new Dictionary<string, string>();
      parameters["peer_id"] = (chatId >= 2000000000L ? chatId : chatId + 2000000000L).ToString();
      parameters["user_id"] = users[index].ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>(method, parameters, res =>
      {
        if (res.ResultCode != ResultCode.Succeeded)
          callback(res);
        else
          ChangeMembers(chatId, users, index + 1, method, callback);
      }, JsonConvert.DeserializeObject<ResponseWithId>);
    }
  }
}
