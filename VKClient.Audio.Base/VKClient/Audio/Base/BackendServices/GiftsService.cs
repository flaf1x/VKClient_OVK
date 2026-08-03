using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VKClient.Audio.Base.DataObjects;
using VKClient.Audio.Base.Events;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;

namespace VKClient.Audio.Base.BackendServices
{
  public class GiftsService
  {
    private static GiftsService _instance;

    public static GiftsService Instance
    {
      get
      {
        return GiftsService._instance ?? (GiftsService._instance = new GiftsService());
      }
    }

    public void GetCatalog(long userId, Action<BackendResult<GiftsCatalogResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      if (userId > 0L)
        parameters["user_id"] = userId.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<GiftsCatalogResponse>("execute.getGiftsCatalog", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetCatalog(long userId, string categoryName, Action<BackendResult<List<GiftsSection>, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      if (userId > 0L)
        parameters["user_id"] = userId.ToString();
      if (!string.IsNullOrEmpty(categoryName))
        parameters["filters"] = categoryName;
      VKRequestsDispatcher.DispatchRequestToVK<List<GiftsSection>>("gifts.getCatalog", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void Get(long userId, int count, int offset, Action<BackendResult<GiftsResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string index1 = "user_id";
      string str1 = userId.ToString();
      parameters[index1] = str1;
      string index2 = "count";
      // OpenVK currently slices after reading the first page. Increasing the
      // server-side window keeps legacy offset pagination working.
      string str2 = Math.Max(count, offset + count).ToString();
      parameters[index2] = str2;
      string index3 = "offset";
      string str3 = offset.ToString();
      parameters[index3] = str3;
      string index4 = "fields";
      string str4 = "photo_max,can_see_gifts,first_name_gen";
      parameters[index4] = str4;
      VKRequestsDispatcher.DispatchRequestToVK<GiftsResponse>("gifts.get", parameters, (Action<BackendResult<GiftsResponse, ResultCode>>) (result =>
      {
        if (result.ResultCode != ResultCode.Succeeded || result.ResultData == null)
        {
          callback(result);
          return;
        }

        GiftsResponse response = result.ResultData;
        List<GiftItemData> items = response.gifts != null && response.gifts.items != null
          ? response.gifts.items
          : new List<GiftItemData>();
        List<long> profileIds = items
          .Where(item => item != null && item.from_id > 0L)
          .Select(item => item.from_id)
          .Concat(userId > 0L ? new List<long>() { userId } : new List<long>())
          .Distinct()
          .ToList();
        if (profileIds.Count == 0)
        {
          callback(result);
          return;
        }

        UsersService.Instance.GetUsers(profileIds, (Action<BackendResult<List<User>, ResultCode>>) (profilesResult =>
        {
          if (profilesResult.ResultCode == ResultCode.Succeeded)
          {
            response.users = profilesResult.ResultData ?? new List<User>();
            response.user = response.users.FirstOrDefault(user => user.id == userId);
          }
          callback(result);
        }));
      }), (Func<string, GiftsResponse>) (jsonString =>
      {
        VKList<GiftItemData> gifts = Newtonsoft.Json.JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<VKList<GiftItemData>>>(jsonString).response;
        if (gifts == null)
          gifts = new VKList<GiftItemData>();
        if (gifts.items == null)
          gifts.items = new List<GiftItemData>();
        return new GiftsResponse()
        {
          gifts = gifts,
          users = new List<User>(),
          groups = new List<Group>()
        };
      }), false, true, new CancellationToken?(), null);
    }

    public void GetGiftInfo(List<long> userIds, Action<BackendResult<GiftResponse, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string index = "user_ids";
      string str = string.Join<long>(",", (IEnumerable<long>) userIds);
      parameters[index] = str;
      VKRequestsDispatcher.DispatchRequestToVK<GiftResponse>("execute.getGiftInfo", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetGiftInfoFromStore(long productId, long userOrChatId, bool isChat, Action<BackendResult<GiftInfoFromStoreResponse, ResultCode>> callback, CancellationToken? cancellationToken = null)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string index1 = "product_id";
      string str1 = productId.ToString();
      parameters[index1] = str1;
      string index2 = "user_or_chat_id";
      string str2 = userOrChatId.ToString();
      parameters[index2] = str2;
      string index3 = "is_chat";
      string str3 = (isChat ? 1 : 0).ToString();
      parameters[index3] = str3;
      VKRequestsDispatcher.DispatchRequestToVK<GiftInfoFromStoreResponse>("execute.getGiftInfoFromStore", parameters, callback,  null, false, true, cancellationToken,  null);
    }

    public void Delete(long id, string giftHash, Action<BackendResult<int, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string index1 = "id";
      string str1 = id.ToString();
      parameters[index1] = str1;
      string index2 = "gift_hash";
      string str2 = giftHash;
      parameters[index2] = str2;
      VKRequestsDispatcher.DispatchRequestToVK<int>("gifts.delete", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetChatUsers(long chatId, Action<BackendResult<List<long>, ResultCode>> callback, CancellationToken? cancellationToken = null)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string index = "chat_id";
      string str = chatId.ToString();
      parameters[index] = str;
      VKRequestsDispatcher.DispatchRequestToVK<List<long>>("execute.getGiftChatUsers", parameters, callback,  null, false, true, cancellationToken,  null);
    }

    public void GetChatUsersForProduct(long chatId, long productId, Action<BackendResult<List<long>, ResultCode>> callback, CancellationToken? cancellationToken = null)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string index1 = "chat_id";
      string str1 = chatId.ToString();
      parameters[index1] = str1;
      string index2 = "product_id";
      string str2 = productId.ToString();
      parameters[index2] = str2;
      VKRequestsDispatcher.DispatchRequestToVK<List<long>>("execute.getGiftChatUsersForProduct", parameters, callback,  null, false, true, cancellationToken,  null);
    }
  }
}
