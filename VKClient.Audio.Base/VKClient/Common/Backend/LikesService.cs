using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VKClient.Audio.Base.Events;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;

namespace VKClient.Common.Backend
{
  public class LikesService
  {
    private static LikesService _current = new LikesService();

    public static LikesService Current
    {
      get
      {
        return LikesService._current;
      }
    }

    public void AddRemoveLike(bool add, long owner_id, long item_id, LikeObjectType objectType, Action<BackendResult<ResponseWithId, ResultCode>> callback, string accessKey = "")
    {
      string methodName = add ? "likes.add" : "likes.delete";
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary["owner_id"] = owner_id.ToString();
      dictionary["item_id"] = item_id.ToString();
      dictionary["type"] = objectType.ToString();
      if (!string.IsNullOrEmpty(accessKey))
        dictionary["access_key"] = accessKey;
      EventAggregator.Current.Publish(new ObjectLikedUnlikedEvent()
      {
        Liked = add,
        ItemId = item_id,
        OwnerId = owner_id,
        LikeObjType = objectType
      });
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<ResponseWithId, ResultCode>> callback1 = (Action<BackendResult<ResponseWithId, ResultCode>>) (res =>
      {
        int resultCode = (int) res.ResultCode;
        callback(res);
      });
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>(methodName, parameters, callback1, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void GetLikesList(LikeObjectType type, long owner_id, long item_id, int count, int offset, bool onlyCopies, bool onlyFriends, Action<BackendResult<LikesList, ResultCode>> callback)
    {
      // OpenVK exposes a VK-compatible direct endpoint, but does not run the
      // legacy multi-call execute script used by the original client.
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["type"] = type.ToString();
      parameters["owner_id"] = owner_id.ToString();
      parameters["item_id"] = item_id.ToString();
      parameters["count"] = count.ToString();
      parameters["offset"] = offset.ToString();
      parameters["filter"] = onlyCopies ? "copies" : "likes";
      parameters["friends_only"] = onlyFriends ? "1" : "0";
      parameters["extended"] = "1";

      VKRequestsDispatcher.DispatchRequestToVK<LikesList>("likes.getList", parameters,
        result =>
        {
          if (result.ResultCode != ResultCode.Succeeded || result.ResultData == null)
          {
            callback(result);
            return;
          }

          List<long> missingUserIds = result.ResultData.AllIds
            .Where(id => id > 0L && !result.ResultData.All.Any(user => user.uid == id))
            .ToList();
          if (missingUserIds.Count == 0)
          {
            callback(result);
            return;
          }

          // Extended responses are expected, but older OpenVK installations
          // can still return only ids. Resolve those ids before the list VM
          // receives the result.
          UsersService.Instance.GetUsers(missingUserIds, usersResult =>
          {
            if (usersResult.ResultCode == ResultCode.Succeeded && usersResult.ResultData != null)
              result.ResultData.All.AddRange(usersResult.ResultData);
            callback(result);
          });
        },
        jsonStr => ParseLikesList(jsonStr), false, true, new CancellationToken?(), null);
    }

    private static LikesList ParseLikesList(string jsonStr)
    {
      JObject root = JObject.Parse(jsonStr);
      JObject response = root["response"] as JObject ?? new JObject();
      LikesList result = new LikesList()
      {
        AllCount = (int?) response["count"] ?? 0,
        All = new List<User>(),
        AllGroups = new List<Group>(),
        AllIds = new List<long>()
      };

      JArray items = response["items"] as JArray ?? new JArray();
      foreach (JToken item in items)
      {
        if (item.Type == JTokenType.Integer)
        {
          result.AllIds.Add((long) item);
          continue;
        }

        JObject profile = item as JObject;
        if (profile == null)
          continue;
        long id = (long?) profile["id"] ?? 0L;
        if (id == 0L)
          continue;
        result.AllIds.Add(id);
        if (profile["first_name"] != null || profile["last_name"] != null)
          result.All.Add(profile.ToObject<User>());
        else if (profile["name"] != null)
          result.AllGroups.Add(profile.ToObject<Group>());
      }
      return result;
    }
  }
}
