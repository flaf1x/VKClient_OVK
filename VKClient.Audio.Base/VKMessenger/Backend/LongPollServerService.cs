using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Utils;

namespace VKMessenger.Backend
{
  public class LongPollServerService : ILongPollServerService
  {
    private readonly string _getUpdatesURIFrm = "{0}{1}act=a_check&key={2}&ts={3}&wait=25&mode=66&version=2";
    private static LongPollServerService _instance;

    public static LongPollServerService Instance
    {
      get
      {
        if (LongPollServerService._instance == null)
          LongPollServerService._instance = new LongPollServerService();
        return LongPollServerService._instance;
      }
    }

    public void GetLongPollServer(Action<BackendResult<LongPollServerResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["use_ssl"] = "1";
      parameters["lp_version"] = "2";
      VKRequestsDispatcher.DispatchRequestToVK<LongPollServerResponse>("messages.getLongPollServer", parameters, callback, new Func<string, LongPollServerResponse>(this.GetLongPollResponseForJson), false, false, new CancellationToken?(),  null);
    }

    public bool HaveToken()
    {
      return VKRequestsDispatcher.HaveToken;
    }

    public void GetUpdates(string serverName, string key, long ts, Action<BackendResult<UpdatesResponse, LongPollResultCode>> callback)
    {
      string serverUrl = serverName ?? "";
      Uri absoluteServer;
      if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out absoluteServer))
        serverUrl = "https://" + serverUrl.TrimStart('/');
      serverUrl = serverUrl.TrimEnd('/');
      // Silverlight can reuse an empty GET response when ts has not changed.
      // A unique URL forces the next check to reach the long-poll server.
      string pollUrl = string.Format(this._getUpdatesURIFrm, serverUrl, serverUrl.Contains("?") ? "&" : "?", Uri.EscapeDataString(key ?? ""), ts)
        + "&_=" + Guid.NewGuid().ToString("N");
      JsonWebRequest.SendHTTPRequestAsync(pollUrl, (Action<JsonResponseData>) (jsonResp =>
      {
        BackendResult<UpdatesResponse, LongPollResultCode> backendResult = new BackendResult<UpdatesResponse, LongPollResultCode>();
        if (!jsonResp.IsSucceeded || string.IsNullOrWhiteSpace(jsonResp.JsonString))
          backendResult.ResultCode = LongPollResultCode.CommunicationFailed;
        else
        {
          try
          {
            JObject root = JObject.Parse(jsonResp.JsonString);
            int failed = (int?)root["failed"] ?? 0;
            if (failed == 1 && root["ts"] != null)
            {
              backendResult.ResultCode = LongPollResultCode.Succeeded;
              backendResult.ResultData = new UpdatesResponse() { ts = (long)root["ts"], Updates = new List<LongPollServerUpdateData>()
              {
                new LongPollServerUpdateData() { UpdateType = LongPollServerUpdateType.SynchronizationRequired }
              } };
            }
            else if (failed != 0)
              backendResult.ResultCode = LongPollResultCode.RequireNewPollServer;
            else if (root["ts"] == null || !(root["updates"] is JArray))
              backendResult.ResultCode = LongPollResultCode.CommunicationFailed;
            else
            {
              RootObjectGetUpdates response = root.ToObject<RootObjectGetUpdates>();
              backendResult.ResultCode = LongPollResultCode.Succeeded;
              backendResult.ResultData = new UpdatesResponse() { ts = response.ts,
                Updates = this.ReadUpdatesResponseFromRaw(response.updates, this.GetUpdateDataForNewMessageLongPollData) };
            }
          }
          catch (Exception ex)
          {
            Logger.Instance.Error("Invalid long poll response", ex);
            backendResult.ResultCode = LongPollResultCode.CommunicationFailed;
          }
        }
        callback(backendResult);
      }),  null);
    }

    private List<LongPollServerUpdateData> ReadUpdatesResponseFromRaw(List<List<object>> rawUpdates, Func<List<object>, LongPollServerUpdateData> getUpdatesForNewMessageFunc)
    {
      List<LongPollServerUpdateData> serverUpdateDataList1 = new List<LongPollServerUpdateData>();
      if (rawUpdates != null)
      {
        foreach (List<object> rawUpdate in rawUpdates)
        {
          if (rawUpdate != null && rawUpdate.Count > 0)
          {
            try
            {
            int num1 = int.Parse(rawUpdate.First<object>().ToString());
            if (num1 == 80)
            {
              int result = 0;
              if (rawUpdate.Count > 1 && int.TryParse(rawUpdate[1].ToString(), out result))
                serverUpdateDataList1.Add(new LongPollServerUpdateData()
                {
                  UpdateType = LongPollServerUpdateType.NewCounter,
                  Counter = result
                });
            }
            if (num1 == 9 || num1 == 8)
            {
              int num2 = 7;
              if (rawUpdate.Count > 2)
                num2 = int.Parse(rawUpdate[2].ToString()) % 256;
              long num3 = -long.Parse(rawUpdate[1].ToString());
              serverUpdateDataList1.Add(new LongPollServerUpdateData()
              {
                UpdateType = (LongPollServerUpdateType) num1,
                user_id = num3,
                Platform = num2
              });
            }
            if (num1 == 6 || num1 == 7)
            {
              long num2 = long.Parse(rawUpdate[1].ToString());
              LongPollServerUpdateData serverUpdateData = new LongPollServerUpdateData()
              {
                UpdateType = num1 == 6 ? LongPollServerUpdateType.IncomingMessagesRead : LongPollServerUpdateType.OutgoingMessagesRead,
                isChat = num2 >= 2000000000L
              };
              if (num2 >= 2000000000L)
                serverUpdateData.chat_id = num2 - 2000000000L;
              else
                serverUpdateData.user_id = num2;
              serverUpdateData.message_id = long.Parse(rawUpdate[2].ToString());
              serverUpdateDataList1.Add(serverUpdateData);
            }
            if (num1 == 4 || num1 == 5)
            {
              LongPollServerUpdateData serverUpdateData = getUpdatesForNewMessageFunc(rawUpdate);
              if (serverUpdateData != null)
              {
                if (num1 == 5)
                  serverUpdateData.UpdateType = LongPollServerUpdateType.MessageHasBeenEdited;
                serverUpdateDataList1.Add(serverUpdateData);
              }
            }
            if (num1 == 3)
            {
              int num2 = int.Parse(rawUpdate[2].ToString());
              int num3 = 1;
              if ((num2 & num3) == 1)
              {
                long num4 = long.Parse(rawUpdate[1].ToString());
                if (rawUpdate.Count >= 4)
                {
                  long num5 = long.Parse(rawUpdate[3].ToString());
                  if (num5 <= 2000000000L && num5 > 0L)
                  {
                    serverUpdateDataList1.Add(new LongPollServerUpdateData()
                    {
                      message_id = num4,
                      user_id = num5,
                      UpdateType = LongPollServerUpdateType.MessageHasBeenRead
                    });
                  }
                  else
                  {
                    List<LongPollServerUpdateData> serverUpdateDataList2 = serverUpdateDataList1;
                    LongPollServerUpdateData serverUpdateData = new LongPollServerUpdateData();
                    serverUpdateData.message_id = num4;
                    long num6 = num5 - 2000000000L;
                    serverUpdateData.chat_id = num6;
                    int num7 = 1;
                    serverUpdateData.isChat = num7 != 0;
                    int num8 = 10;
                    serverUpdateData.UpdateType = (LongPollServerUpdateType) num8;
                    serverUpdateDataList2.Add(serverUpdateData);
                  }
                }
              }
              int num9 = 128;
              if ((num2 & num9) == 128)
              {
                LongPollServerUpdateData serverUpdateData = this.ReadUserOrChatIds(rawUpdate);
                if (serverUpdateData != null)
                {
                  serverUpdateData.UpdateType = LongPollServerUpdateType.MessageHasBeenRestored;
                  serverUpdateDataList1.Add(serverUpdateData);
                }
              }
            }
            if (num1 == 2 && (int.Parse(rawUpdate[2].ToString()) & 128) == 128)
            {
              LongPollServerUpdateData serverUpdateData = this.ReadUserOrChatIds(rawUpdate);
              if (serverUpdateData != null)
              {
                serverUpdateData.UpdateType = LongPollServerUpdateType.MessageHasBeenDeleted;
                serverUpdateDataList1.Add(serverUpdateData);
              }
            }
            if (num1 == 51)
            {
              long num2 = long.Parse(rawUpdate[1].ToString());
              serverUpdateDataList1.Add(new LongPollServerUpdateData()
              {
                UpdateType = LongPollServerUpdateType.ChatParamsWereChanged,
                isChat = true,
                chat_id = num2
              });
            }
            if (num1 == 61)
            {
              long num2 = long.Parse(rawUpdate[1].ToString());
              serverUpdateDataList1.Add(new LongPollServerUpdateData()
              {
                UpdateType = LongPollServerUpdateType.UserIsTyping,
                user_id = num2
              });
            }
            if (num1 == 114 && rawUpdate.Count >= 4)
            {
              long peer = long.Parse(rawUpdate[1].ToString());
              serverUpdateDataList1.Add(new LongPollServerUpdateData {
                UpdateType = LongPollServerUpdateType.NotificationSettingsChanged,
                isChat = peer >= 2000000000L,
                chat_id = peer >= 2000000000L ? peer - 2000000000L : 0,
                user_id = peer < 2000000000L ? peer : 0,
                NotificationSound = rawUpdate[2].ToString().Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 :
                  rawUpdate[2].ToString().Equals("false", StringComparison.OrdinalIgnoreCase) ? 0 : int.Parse(rawUpdate[2].ToString()),
                NotificationsDisabledUntil = int.Parse(rawUpdate[3].ToString())
              });
            }
            if (num1 == 62)
            {
              long num2 = long.Parse(rawUpdate[1].ToString());
              long num3 = long.Parse(rawUpdate[2].ToString());
              serverUpdateDataList1.Add(new LongPollServerUpdateData()
              {
                UpdateType = LongPollServerUpdateType.UserIsTypingInChat,
                user_id = num2,
                chat_id = num3,
                isChat = true
              });
            }
            }
            catch (Exception ex)
            {
              Logger.Instance.Error("Skipping malformed long poll event", ex);
            }
          }
        }
      }
      return serverUpdateDataList1;
    }

    private LongPollServerUpdateData ReadUserOrChatIds(List<object> updateDataRaw)
    {
      if (updateDataRaw == null || updateDataRaw.Count < 4)
        return  null;
      long num1 = long.Parse(updateDataRaw[1].ToString());
      bool flag = false;
      long num2 = 0;
      long num3 = 0;
      if (updateDataRaw.Count >= 4)
      {
        long num4 = long.Parse(updateDataRaw[3].ToString());
        if (num4 - 2000000000L >= 0L)
        {
          flag = true;
          num3 = num4 - 2000000000L;
        }
        else
        {
          flag = false;
          num2 = num4;
        }
      }
      return new LongPollServerUpdateData()
      {
        user_id = num2,
        chat_id = num3,
        isChat = flag,
        message_id = num1
      };
    }

    private LongPollServerUpdateData GetUpdateDataForNewMessageLongPollData(List<object> updateDataRaw)
    {
      try
      {
        LongPollServerUpdateData serverUpdateData = new LongPollServerUpdateData();
        serverUpdateData.UpdateType = LongPollServerUpdateType.MessageHasBeenAdded;
        long num1 = long.Parse(updateDataRaw[1].ToString());
        bool flag1 = (long.Parse(updateDataRaw[2].ToString()) & 2L) == 2L;
        long num2 = long.Parse(updateDataRaw[3].ToString());
        long num3 = long.Parse(updateDataRaw[4].ToString());
        // VK/OpenVK long poll event 4 stores the message text at index 5.
        // Index 6 is the additional-info object; reading it displayed "{}".
        string str = updateDataRaw.Count > 5 && updateDataRaw[5] != null
          ? updateDataRaw[5].ToString()
          : "";
        bool flag2 = false;
        bool flag3 = num2 >= 2000000000L;
        long num4 = flag3 ? num2 - 2000000000L : 0;
        if (updateDataRaw.Count > 6)
        {
          JObject additionalInfo = updateDataRaw[6] as JObject;
          if (additionalInfo != null)
          {
            foreach (KeyValuePair<string, JToken> keyValuePair in additionalInfo)
            {
              if (keyValuePair.Key == "from" && flag3)
              {
                num2 = long.Parse(keyValuePair.Value.ToString());
              }
              else if (keyValuePair.Key == "fwd" || keyValuePair.Key == "attach1_type")
                flag2 = true;
            }
          }
        }
        JObject attachments = updateDataRaw.Count > 7 ? updateDataRaw[7] as JObject : null;
        if (attachments != null && attachments.HasValues)
          flag2 = true;
        serverUpdateData.message_id = num1;
        serverUpdateData.@out = flag1;
        serverUpdateData.user_id = num2;
        serverUpdateData.chat_id = num4;
        serverUpdateData.timestamp = num3;
        serverUpdateData.text = str;
        serverUpdateData.isChat = flag3;
        serverUpdateData.hasAttachOrForward = flag2;
        return serverUpdateData;
      }
      catch (Exception ex)
      {
        Logger.Instance.Error("GetUpdateDataForNewMessage", ex);
        return  null;
      }
    }

    private LongPollServerResponse GetLongPollResponseForJson(string jsonStr)
    {
      return JsonConvert.DeserializeObject<LongPollServerService.RootObjectGetServer>(jsonStr).response;
    }

    private bool ServerReturnedFailure(string jsonStr)
    {
      return jsonStr.StartsWith("{\"failed");
    }

    public class RootObjectGetServer
    {
      public LongPollServerResponse response { get; set; }
    }

    public class RootObjectGetUpdates
    {
      public long ts { get; set; }

      public List<List<object>> updates { get; set; }
    }
  }
}
