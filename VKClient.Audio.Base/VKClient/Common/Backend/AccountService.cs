using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using VKClient.Audio.Base;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Library;
using VKClient.Common.Utils;

namespace VKClient.Common.Backend
{
  public class AccountService
  {
    private static AccountService _instance;

    public static AccountService Instance
    {
      get
      {
        if (AccountService._instance == null)
          AccountService._instance = new AccountService();
        return AccountService._instance;
      }
    }

    public void DeleteProfilePhoto(Action<BackendResult<User, ResultCode>> callback)
    {
      VKRequestsDispatcher.Execute<User>("var ph = API.photos.get({album_id:-6, rev:1, count:1}).items[0];\r\nAPI.photos.delete({owner_id:ph.owner_id, photo_id:ph.id});\r\nreturn API.users.get({fields:\"photo_max\"})[0];", callback,  null, false, true, new CancellationToken?());
    }

    public void RegisterDevice(string deviceId, string token, string deviceModelViewable, string systemVersion, string appVersion, string settings, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      // OpenVK has no mobile push registration endpoint. Complete locally to
      // prevent the original retry loop from treating this as connectivity loss.
      callback(new BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>(
        ResultCode.Succeeded,
        new VKClient.Common.Backend.DataObjects.ResponseWithId() { response = 1 }));
    }

    public void UnregisterDevice(string deviceId, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      callback(new BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>(
        ResultCode.Succeeded,
        new VKClient.Common.Backend.DataObjects.ResponseWithId() { response = 1 }));
    }

    public void SetPushSettings(string deviceId, string key, string value, string key2, string value2, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      callback(new BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>(
        ResultCode.Succeeded,
        new VKClient.Common.Backend.DataObjects.ResponseWithId() { response = 1 }));
    }

    public void CheckShortName(string shortName, Action<BackendResult<CheckNameResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["screen_name"] = shortName;
      parameters["suggestions"] = "1";
      VKRequestsDispatcher.DispatchRequestToVK<CheckNameResponse>("utils.checkScreenName", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void SetShortName(string shortName, Action<BackendResult<SaveProfileResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["screen_name"] = shortName;
      VKRequestsDispatcher.DispatchRequestToVK<SaveProfileResponse>("account.saveProfileInfo", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void SetInfo(string name, string val, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["name"] = name;
      parameters["value"] = val;
      VKRequestsDispatcher.DispatchRequestToVK<VKClient.Common.Backend.DataObjects.ResponseWithId>("account.setInfo", parameters, callback, (Func<string, VKClient.Common.Backend.DataObjects.ResponseWithId>) (jsonStr => new VKClient.Common.Backend.DataObjects.ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void SetInfo(bool? showOwnPostsDefault, bool? noWallReplies, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      if (showOwnPostsDefault.HasValue)
        parameters["own_posts_default"] = showOwnPostsDefault.Value ? "1" : "0";
      if (noWallReplies.HasValue)
        parameters["no_wall_replies"] = noWallReplies.Value ? "1" : "0";
      VKRequestsDispatcher.DispatchRequestToVK<VKClient.Common.Backend.DataObjects.ResponseWithId>("account.setInfo", parameters, callback, (Func<string, VKClient.Common.Backend.DataObjects.ResponseWithId>) (jsonStr => new VKClient.Common.Backend.DataObjects.ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void ChangePassword(string oldPassword, string newPassword, Action<BackendResult<ChangePasswordResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["old_password"] = oldPassword;
      parameters["new_password"] = newPassword;
      VKRequestsDispatcher.DispatchRequestToVK<ChangePasswordResponse>("account.changePassword", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetSettingsAccountInfo(Action<BackendResult<SettingsAccountInfo, ResultCode>> callback)
    {
      VKRequestsDispatcher.Execute<SettingsAccountInfo>("var account = API.account.getInfo();\r\n\r\nvar newsBanned = API.newsfeed.getBanned();\r\n\r\nvar pi = API.account.getProfileInfo();\r\n\r\nreturn {\"Account\": account, \"NewsBanned\":newsBanned, \"ProfileInfo\":pi};", callback,  null, false, true, new CancellationToken?());
    }

    public void SaveSettingsAccountInfo(Dictionary<string, string> parameters, Action<BackendResult<SaveProfileResponse, ResultCode>> callback, UploadPhotoResponseData newPhotoData = null)
    {
      if (parameters.Count == 0)
      {
        if (newPhotoData == null)
          return;
        UsersService.Instance.SaveProfilePhoto(newPhotoData, (Action<BackendResult<ProfilePhoto, ResultCode>>) (pres => callback(new BackendResult<SaveProfileResponse, ResultCode>(pres.ResultCode, new SaveProfileResponse()))));
      }
      else
        VKRequestsDispatcher.DispatchRequestToVK<SaveProfileResponse>("account.saveProfileInfo", parameters, (Action<BackendResult<SaveProfileResponse, ResultCode>>) (res =>
        {
          if (res.ResultCode == ResultCode.Succeeded && newPhotoData != null)
            UsersService.Instance.SaveProfilePhoto(newPhotoData, (Action<BackendResult<ProfilePhoto, ResultCode>>) (pres => callback(res)));
          else
            callback(res);
        }),  null, false, true, new CancellationToken?(),  null);
    }

    public void GetSettingsProfileInfo(Action<BackendResult<ProfileInfo, ResultCode>> callback)
    {
      VKRequestsDispatcher.Execute<ProfileInfo>("var pi = API.account.getProfileInfo();\r\nvar u = API.users.get({\"fields\":\"photo_max\"});\r\nvar partner;\r\nif (pi.relation_partner + \"\"!=\"\")\r\n{\r\n    partner = API.users.get({\"user_ids\":pi.relation_partner.id, \"fields\":\"photo_max,first_name_gen,last_name_gen,sex\"});\r\n    return {\"User\":u[0], \"ProfileInfo\":pi, \"Partner\":partner[0]};\r\n}\r\nvar relation_requests;\r\nif (pi.relation_requests + \"\"!=\"\")\r\n{\r\n    relation_requests = API.users.get({\"user_ids\":pi.relation_requests@.id, \"fields\":\"sex\"});\r\n}\r\n\r\nreturn {\"User\":u[0], \"ProfileInfo\":pi, \"RelationRequests\":relation_requests};", callback, (Func<string, ProfileInfo>) (jsonStr =>
      {
        GetProfileInfoResponse response = JsonConvert.DeserializeObject<GenericRoot<GetProfileInfoResponse>>(jsonStr).response;
        response.ProfileInfo.photo_max = response.User.photo_max;
        if (response.partner != null && response.ProfileInfo.relation_partner != null)
        {
          response.ProfileInfo.relation_partner.photo_max = response.partner.photo_max;
          response.ProfileInfo.relation_partner.first_name_gen = response.partner.first_name_gen;
          response.ProfileInfo.relation_partner.last_name_gen = response.partner.last_name_gen;
          response.ProfileInfo.relation_partner.sex = response.partner.sex;
        }
        if (response.RelationRequests != null)
          response.ProfileInfo.relation_requests = response.RelationRequests;
        return response.ProfileInfo;
      }), false, true, new CancellationToken?());
    }

    public void GetPrivacySettings(Action<BackendResult<PrivacySettingsInfo, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<PrivacySettingsInfo>("account.getPrivacySettings", new Dictionary<string, string>(), callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void SetPrivacy(string key, string value, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["key"] = key;
      parameters["value"] = value;
      VKRequestsDispatcher.DispatchRequestToVK<VKClient.Common.Backend.DataObjects.ResponseWithId>("account.setPrivacy", parameters, callback, (Func<string, VKClient.Common.Backend.DataObjects.ResponseWithId>) (jsonStr => new VKClient.Common.Backend.DataObjects.ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void TestValidation(Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<VKClient.Common.Backend.DataObjects.ResponseWithId>("account.testValidation", new Dictionary<string, string>(), callback, (Func<string, VKClient.Common.Backend.DataObjects.ResponseWithId>) (jsonResp => new VKClient.Common.Backend.DataObjects.ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void GetBannedUsers(int offset, int count, Action<BackendResult<VKList<User>, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["offset"] = offset.ToString();
      parameters["count"] = count.ToString();
      parameters["fields"] = "photo_max";
      VKRequestsDispatcher.DispatchRequestToVK<VKList<User>>("account.getBanned", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void BanUser(long user_id, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["owner_id"] = user_id.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<VKClient.Common.Backend.DataObjects.ResponseWithId>("account.ban", parameters, callback, (Func<string, VKClient.Common.Backend.DataObjects.ResponseWithId>) (jsonStr => new VKClient.Common.Backend.DataObjects.ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void UnbanUsers(List<long> user_ids, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      string format = "API.account.unban({{\"owner_id\":{0}}});";
      string code = "";
      foreach (long userId in user_ids)
        code = code + string.Format(format, userId) + Environment.NewLine;
      VKRequestsDispatcher.Execute<VKClient.Common.Backend.DataObjects.ResponseWithId>(code, callback, (Func<string, VKClient.Common.Backend.DataObjects.ResponseWithId>) (jsonStr => new VKClient.Common.Backend.DataObjects.ResponseWithId()), false, true, new CancellationToken?());
    }

    public void StatsTrackEvents(List<AppEventBase> appEvents, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      if (callback != null)
        callback(new BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>(
          ResultCode.Succeeded,
          new VKClient.Common.Backend.DataObjects.ResponseWithId()));
    }

    public void InternalGetNotifications(string device, string os, string appVersion, string locale, Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["device"] = device;
      parameters["os"] = os;
      parameters["app_version"] = appVersion;
      parameters["locale"] = locale;
      VKRequestsDispatcher.DispatchRequestToVK<VKClient.Common.Backend.DataObjects.ResponseWithId>("internal.getNotifications", parameters, callback, (Func<string, VKClient.Common.Backend.DataObjects.ResponseWithId>) (jsonStr => new VKClient.Common.Backend.DataObjects.ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void SetUserOnline(Action<BackendResult<object, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<object>("account.setOnline", new Dictionary<string, string>(), (Action<BackendResult<object, ResultCode>>) (res => callback(res)),  null, true, true, new CancellationToken?(),  null);
    }

    public void SetSilenceMode(string device_id, int nrOfSeconds, Action<BackendResult<object, ResultCode>> callback, long chatId = 0, long uid = 0)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string str = nrOfSeconds.ToString();
      // Peer settings must be account-wide, not scoped to this push device.
      if (chatId == 0 && uid == 0 && !string.IsNullOrEmpty(device_id))
        parameters.Add("token", device_id);
      parameters.Add("time", str);
      parameters.Add("sound", nrOfSeconds == 0 ? "1" : "0");
      if (chatId != 0L)
        parameters["peer_id"] = MessagesService.Instance.GetPeerId(chatId, true).ToString();
      if (uid != 0L)
        parameters["peer_id"] = MessagesService.Instance.GetPeerId(uid, false).ToString();
      VKRequestsDispatcher.DispatchRequestToVK<object>("account.setSilenceMode", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetPeerPushSettings(long peerId, Action<BackendResult<VKMessenger.Backend.PushSettings, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<VKMessenger.Backend.PushSettings>("account.getPushSettings",
        new Dictionary<string, string> { { "peer_id", peerId.ToString() } }, callback);
    }

    public void GetCounters(Action<BackendResult<OwnCounters, ResultCode>> callback)
    {
      string methodName = "account.getCounters";
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      Action<BackendResult<OwnCounters, ResultCode>> callback1 = callback;
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<OwnCounters>(methodName, parameters, callback1, (Func<string, OwnCounters>) (jsonStr => CountersDeserializerHelper.Deserialize(jsonStr)), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void GetBaseData(PhoneAppInfo phoneAppInfo, Action<BackendResult<AccountBaseData, ResultCode>> callback, bool loadStickersInfo = false)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        {
          "fields",
          "photo_50,photo_100,photo_200,photo_max,exports,country,city,sex,status,activity,bdate,first_name_gen,last_name_gen,verified,counters,online,last_seen,screen_name"
        }
      };

      VKRequestsDispatcher.DispatchRequestToVK<AccountBaseData>("execute.getUserInfo", parameters, callback, (Func<string, AccountBaseData>) (jsonStr =>
      {
        JObject response = JObject.Parse(jsonStr)["response"] as JObject;
        if (response == null)
          return new AccountBaseData()
          {
            User = new User(),
            OwnCounters = new OwnCounters(),
            Info = new AccountBaseInfo(),
            NewsData = new NewsFeedData()
          };

        AccountBaseInfo info = response["info"] != null
          ? response["info"].ToObject<AccountBaseInfo>()
          : new AccountBaseInfo();
        JObject profilerSettings = response["info"] != null
          ? response["info"]["profiler_settings"] as JObject
          : null;
        if (info.settings == null && profilerSettings != null && profilerSettings["settings"] != null)
          info.settings = profilerSettings["settings"].ToObject<List<AccountBaseInfoSettingsEntry>>();

        User profile = response["profile"] != null
          ? response["profile"].ToObject<User>()
          : new User();
        if (profile.id == 0L)
          profile.id = VKRequestsDispatcher.AuthData.user_id;

        return new AccountBaseData()
        {
          User = profile,
          OwnCounters = response["counters"] != null ? response["counters"].ToObject<OwnCounters>() : new OwnCounters(),
          NewsData = response["newsfeed"] != null ? response["newsfeed"].ToObject<NewsFeedData>() : new NewsFeedData(),
          Info = info,
          time = response["time"] != null ? response["time"].Value<int>() : 0,
          GamesSectionEnabled = 0,
          DebugDisabled = 1
        };
      }), true, true, new CancellationToken?(),  null);
    }

    public void GetIntermediateData(Action<BackendResult<AccountIntermediateData, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<AccountIntermediateData>(
        "account.setOnline",
        new Dictionary<string, string>(),
        callback,
        (Func<string, AccountIntermediateData>) (jsonStr => new AccountIntermediateData()),
        true,
        true,
        new CancellationToken?(),
        null);
    }

    public void ResolveScreenName(string name, Action<BackendResult<ResolvedData, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<ResolvedData>("utils.resolveScreenName", new Dictionary<string, string>()
      {
        {
          "screen_name",
          name
        }
      }, callback, jsonStr =>
      {
        ResolvedObject resolved = JsonConvert.DeserializeObject<GenericRoot<ResolvedObject>>(jsonStr).response;
        return new ResolvedData() { resolvedObject = resolved };
      }, false, true, new CancellationToken?(), null);
    }

    public void StatusSet(string text, string audio, long groupId, Action<BackendResult<long, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      if (!string.IsNullOrEmpty(audio))
        parameters["audio"] = audio;
      else
        parameters["text"] = text;
      if (groupId > 0L)
        parameters["group_id"] = groupId.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<long>("status.set", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void LookupContacts(string service, string myContact, List<string> contacts, Action<BackendResult<LookupContactsResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["service"] = service;
      parameters["fields"] = "photo_max,verified,occupation,city,country,friend_status,common_count";
      if (!string.IsNullOrEmpty(myContact))
        parameters["mycontact"] = myContact;
      if (!contacts.IsNullOrEmpty())
        parameters["contacts"] = string.Join(",", (IEnumerable<string>) contacts);
      VKRequestsDispatcher.DispatchRequestToVK<LookupContactsResponse>("account.lookupContacts", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }
  }
}
