using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKMessenger.Backend;

namespace VKClient.Audio.Base
{
  public class CountersService : ICountersService
  {
    private static CountersService _instance;

    public static CountersService Instance
    {
      get
      {
        if (CountersService._instance == null)
          CountersService._instance = new CountersService();
        return CountersService._instance;
      }
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

    public void GetCountersWithLastMessage(Action<BackendResult<CountersWithMessageInfo, ResultCode>> callback)
    {
      this.GetCounters((Action<BackendResult<OwnCounters, ResultCode>>) (counterResult =>
      {
        if (counterResult.ResultCode != ResultCode.Succeeded)
        {
          callback(new BackendResult<CountersWithMessageInfo, ResultCode>(
            counterResult.ResultCode));
          return;
        }
        MessagesService.Instance.GetDialogs(new GetDialogsRequest(0, 1, 100),
          (Action<BackendResult<MessageListResponse, ResultCode>>) (dialogsResult =>
          {
            CountersWithMessageInfo result = new CountersWithMessageInfo()
            {
              Counters = counterResult.ResultData
            };
            if (dialogsResult.ResultCode == ResultCode.Succeeded &&
                dialogsResult.ResultData != null &&
                dialogsResult.ResultData.DialogHeaders != null)
            {
              DialogHeaderInfo header =
                dialogsResult.ResultData.DialogHeaders.FirstOrDefault();
              result.LastMessage = header != null ? header.message : null;
              if (result.LastMessage != null &&
                  dialogsResult.ResultData.Users != null)
                result.User = dialogsResult.ResultData.Users.FirstOrDefault(
                  u => u.uid == result.LastMessage.user_id);
            }
            callback(new BackendResult<CountersWithMessageInfo, ResultCode>(
              ResultCode.Succeeded, result));
          }));
      }));
    }
  }
}
