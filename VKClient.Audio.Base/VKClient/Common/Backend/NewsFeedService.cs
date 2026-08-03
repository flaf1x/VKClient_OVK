using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Library;
using VKClient.Common.Utils;

namespace VKClient.Common.Backend
{
  public class NewsFeedService
  {
    private static NewsFeedService _current = new NewsFeedService();

    public static NewsFeedService Current
    {
      get
      {
        return NewsFeedService._current;
      }
    }

    public Task<BackendResult<NewsFeedData, ResultCode>> GetNewsFeed(NewsFeedGetParams parameters)
    {
      TaskCompletionSource<BackendResult<NewsFeedData, ResultCode>> tcs = new TaskCompletionSource<BackendResult<NewsFeedData, ResultCode>>();
      this.GetNewsFeed(parameters, (Action<BackendResult<NewsFeedData, ResultCode>>) (res => tcs.TrySetResult(res)));
      return tcs.Task;
    }

    public void GetNewsFeed(NewsFeedGetParams parameters, Action<BackendResult<NewsFeedData, ResultCode>> callback)
    {
      // OpenVK exposes the feed through the public VK-compatible method.
      // execute.getNewsfeed is a private method of the original VK mobile API.
      string methodName = "newsfeed.get";
      Dictionary<string, string> paramDict = new Dictionary<string, string>()
      {
        {
          "count",
          parameters.count.ToString()
        },
        {
          "device_info",
          new AdvertisingDeviceInfo().ToJsonString()
        }
      };
      // OpenVK returns this property in the extended feed profiles only when
      // it is requested explicitly; the feed header uses it directly.
      paramDict["fields"] = "sex,online,photo_50,photo_100,photo_200,is_friend,verified";
      if (!string.IsNullOrEmpty(parameters.from))
        paramDict["start_from"] = parameters.from;
      bool? nullable;
      if (parameters.NewsListId == -100L)
      {
        paramDict["filters"] = "video";
        paramDict["grouping"] = "100";
        paramDict["extended"] = "1";
      }
      else if (parameters.photoFeed || parameters.NewsListId == -101L)
      {
        paramDict["filters"] = "photo,photo_tag,wall_photo";
        paramDict["max_photos"] = "10";
        paramDict["extended"] = "1";
        if (parameters.source_ids.NotNullAndHasAtLeastOneNonNullElement())
        {
          paramDict["source_ids"] = parameters.source_ids.GetCommaSeparated();
        }
        else
        {
          paramDict["source_ids"] = "friends,following";
          paramDict["return_banned"] = "1";
        }
      }
      else
      {
        paramDict["filters"] = "post,photo,photo_tag";
        paramDict["extended"] = "1";
        long num1 = parameters.NewsListId - -10L;
        long num2 = 3;
        if ((ulong) num1 <= (ulong) num2)
        {
          switch ((uint) num1)
          {
            case 0:
              paramDict["is_newsfeed"] = "1";
              Dictionary<string, string> dictionary1 = paramDict;
              dictionary1["filters"] = dictionary1["filters"] + ",friends_recomm,ads_post";
              nullable = AppGlobalStateManager.Current.GlobalState.AdsDemoManualSetting;
              bool flag = true;
              if ((nullable.GetValueOrDefault() == flag ? (nullable.HasValue ? 1 : 0) : 0) != 0)
              {
                Dictionary<string, string> dictionary2 = paramDict;
                dictionary2["filters"] = dictionary2["filters"] + ",ads_demo";
                goto label_16;
              }
              else
                goto label_16;
            case 1:
              methodName = "newsfeed.getGlobal";
              goto label_16;
            case 2:
              paramDict["source_ids"] = "friends,following";
              goto label_16;
            case 3:
              paramDict["source_ids"] = "groups,pages";
              goto label_16;
          }
        }
        paramDict["source_ids"] = "list" + parameters.NewsListId;
      }
label_16:
      NewsFeedType? feedType = parameters.FeedType;
      if (feedType.HasValue)
      {
        paramDict["feed_type"] = feedType.Value.ToString();
        if (parameters.UpdateFeedType)
          paramDict["set_feed_type"] = "1";
      }
      if (parameters.SyncNotifications)
        paramDict["sync_notifications"] = "1";
      nullable = parameters.TopFeedPromoAnswer;
      if (nullable.HasValue)
      {
        Dictionary<string, string> dictionary = paramDict;
        string index = "top_feed_promo_accepted";
        nullable = parameters.TopFeedPromoAnswer;
        string str = nullable.Value ? "1" : "0";
        dictionary[index] = str;
        paramDict["top_feed_promo_id"] = parameters.TopFeedPromoId.ToString();
      }
      ConnectionType connectionType = NetworkStatusInfo.Instance.RetrieveNetworkConnectionType();
      if (connectionType != null)
      {
        paramDict["connection_type"] = connectionType.Type;
        paramDict["connection_subtype"] = connectionType.Subtype;
      }
      if (parameters.UpdateAwayTime > 0)
        paramDict["update_away_time"] = parameters.UpdateAwayTime.ToString();
      if (parameters.UpdatePosition > -1)
        paramDict["update_position"] = parameters.UpdatePosition.ToString();
      if (!string.IsNullOrEmpty(parameters.UpdatePost))
        paramDict["update_post"] = parameters.UpdatePost;
      NewsFeedService.DoLoadNewsFeed(methodName, paramDict, callback);
    }

    private static void DoLoadNewsFeed(string methodName, Dictionary<string, string> paramDict, Action<BackendResult<NewsFeedData, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<NewsFeedData>(methodName, paramDict, callback, (Func<string, NewsFeedData>) (jsonStr =>
      {
        List<int> resultCounts1;
        jsonStr = VKRequestsDispatcher.GetArrayCountsAndRemove(jsonStr, "photos", out resultCounts1);
        List<int> resultCounts2;
        jsonStr = VKRequestsDispatcher.GetArrayCountsAndRemove(jsonStr, "photo_tags", out resultCounts2);
        NewsFeedData response = JsonConvert.DeserializeObject<GenericRoot<NewsFeedData>>(jsonStr).response;
        if (response == null)
          response = new NewsFeedData();
        if (response.items == null)
          response.items = new List<NewsItem>();
        if (response.profiles == null)
          response.profiles = new List<User>();
        if (response.groups == null)
          response.groups = new List<Group>();
        // OpenVK paginates with next_from and does not expose a total count.
        // Keep the legacy collection open while a continuation token exists;
        // otherwise it sees count == first page size and stops after 20 items.
        if (!string.IsNullOrEmpty(response.next_from))
          response.count = int.MaxValue;
        else if (response.count == 0)
          response.count = response.items.Count;
        if (string.IsNullOrEmpty(response.feed_type))
          response.feed_type = NewsFeedType.recent.ToString();
        NewsFeedType result1;
        if (Enum.TryParse<NewsFeedType>(response.feed_type, out result1))
          response.FeedType = new NewsFeedType?(result1);
        VKList<UserNotification> notifications = response.notifications;
        if ((notifications != null ? notifications.items :  null) != null)
        {
          foreach (UserNotification userNotification in response.notifications.items)
          {
            UserNotificationType result2;
            if (Enum.TryParse<UserNotificationType>(userNotification.type, out result2))
              userNotification.Type = result2;
          }
        }
        return response;
      }), false, true, new CancellationToken?(),  null);
    }

    public void GetNewsComments(int startTime, int endTime, int count, string fromStr, Action<BackendResult<NewsFeedData, ResultCode>> callback)
    {
      // newsfeed.getComments is not implemented by OpenVK. Keep the comments
      // pivot stable and empty while notifications.get remains functional.
      callback(new BackendResult<NewsFeedData, ResultCode>(
        ResultCode.Succeeded,
        new NewsFeedData()
        {
          items = new List<NewsItem>(),
          profiles = new List<User>(),
          groups = new List<Group>(),
          count = 0
        }));
    }

    public void GetNotifications(int startTime, int endTime, int offset, string fromStr, int count, Action<BackendResult<NotificationData, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      if (startTime > 0)
        parameters["start_time"] = startTime.ToString();
      if (endTime > 0)
        parameters["end_time"] = endTime.ToString();
      if (count > 0)
        parameters["count"] = count.ToString();
      if (offset > 0)
        parameters["offset"] = offset.ToString();
      if (!string.IsNullOrWhiteSpace(fromStr))
        parameters["start_from"] = fromStr;
      parameters["fields"] = "sex,photo_50,photo_100,online,screen_name,first_name_dat,last_name_dat,first_name_gen,last_name_gen";
      Dictionary<string, string> dictionary = parameters;
      dictionary["fields"] = dictionary["fields"] + ",is_closed,type,is_admin,is_member,photo_200";
      VKRequestsDispatcher.DispatchRequestToVK<NotificationData>(
        "notifications.get",
        parameters,
        (Action<BackendResult<NotificationData, ResultCode>>) (result =>
        {
          if (result.ResultCode != ResultCode.Succeeded || result.ResultData == null)
          {
            callback(result);
            return;
          }
          this.AssignKnownNotificationActors(result.ResultData);
          List<int> missingActorIndexes = result.ResultData.items
            .Select((notification, index) => new { notification, index })
            .Where(item => item.notification.actor_id == 0L &&
                           this.NeedsResolvedActor(item.notification.NotType))
            .Select(item => item.index)
            .ToList();
          this.ResolveNotificationActors(
            result, offset, missingActorIndexes, 0, callback);
        }),
        (Func<string, NotificationData>) this.DeserializeNotifications,
        false, true, new CancellationToken?(), null);
    }

    public void FetchNotifications(string lastId, Action<BackendResult<NotificationData, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["last_id"] = string.IsNullOrWhiteSpace(lastId) ? "0" : lastId;
      VKRequestsDispatcher.DispatchRequestToVK<NotificationData>(
        "notifications.fetch",
        parameters,
        (Action<BackendResult<NotificationData, ResultCode>>) (result =>
        {
          if (result.ResultCode != ResultCode.Succeeded ||
              result.ResultData == null ||
              result.ResultData.items == null ||
              result.ResultData.items.Count == 0)
          {
            callback(result);
            return;
          }

          this.AssignKnownNotificationActors(result.ResultData);
          this.ResolveFetchedNotificationActorProfiles(result, callback);
        }),
        (Func<string, NotificationData>) this.DeserializeNotifications,
        false, true, new CancellationToken?(), null);
    }

    private void ResolveFetchedNotificationActorProfiles(
      BackendResult<NotificationData, ResultCode> fetchResult,
      Action<BackendResult<NotificationData, ResultCode>> callback)
    {
      NotificationData data = fetchResult.ResultData;
      List<long> missingActorIds = data.items
        .Select(item => item.actor_id)
        .Where(actorId => actorId != 0L &&
          !data.profiles.Any(user => user.uid == actorId) &&
          !(actorId < 0L && data.groups.Any(group => group.id == -actorId)))
        .Distinct()
        .ToList();

      if (missingActorIds.Count == 0)
      {
        if (data.items.Any(item => item.actor_id == 0L))
          this.EnrichNewestFetchedNotification(fetchResult, callback);
        else
          callback(fetchResult);
        return;
      }

      UsersService.Instance.GetUsers(missingActorIds,
        (Action<BackendResult<List<User>, ResultCode>>) (usersResult =>
        {
          if (usersResult.ResultCode == ResultCode.Succeeded &&
              usersResult.ResultData != null)
          {
            foreach (User user in usersResult.ResultData)
              if (!data.profiles.Any(existing => existing.uid == user.uid))
                data.profiles.Add(user);
          }
          callback(fetchResult);
        }));
    }

    private void EnrichNewestFetchedNotification(
      BackendResult<NotificationData, ResultCode> fetchResult,
      Action<BackendResult<NotificationData, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        { "count", "1" },
        { "fields", "sex,photo_50,photo_100,photo_200,photo_max,screen_name" }
      };
      VKRequestsDispatcher.DispatchRequestToVK<NotificationData>(
        "notifications.get",
        parameters,
        (Action<BackendResult<NotificationData, ResultCode>>) (snapshot =>
        {
          if (snapshot.ResultCode == ResultCode.Succeeded &&
              snapshot.ResultData != null &&
              snapshot.ResultData.items != null &&
              snapshot.ResultData.items.Count > 0)
          {
            this.AssignKnownNotificationActors(snapshot.ResultData);
            Notification fetched = fetchResult.ResultData.items
              .OrderByDescending(item => item.Date)
              .FirstOrDefault();
            Notification full = snapshot.ResultData.items[0];
            if (fetched != null && full != null &&
                fetched.Date == full.Date &&
                string.Equals(fetched.type, full.type, StringComparison.OrdinalIgnoreCase))
            {
              fetched.parent = full.parent;
              fetched.feedback = full.feedback;
              fetched.UpdateNotificationType();
              fetched.actor_id = full.actor_id;

              User actor = snapshot.ResultData.profiles.FirstOrDefault();
              Group actorGroup = snapshot.ResultData.groups.FirstOrDefault();
              if (fetched.actor_id == 0L)
              {
                if (actor != null)
                  fetched.actor_id = actor.uid;
                else if (actorGroup != null)
                  fetched.actor_id = -actorGroup.id;
              }

              foreach (User user in snapshot.ResultData.profiles)
                if (!fetchResult.ResultData.profiles.Any(existing => existing.uid == user.uid))
                  fetchResult.ResultData.profiles.Add(user);
              foreach (Group group in snapshot.ResultData.groups)
                if (!fetchResult.ResultData.groups.Any(existing => existing.id == group.id))
                  fetchResult.ResultData.groups.Add(group);
            }
          }
          callback(fetchResult);
        }),
        (Func<string, NotificationData>) this.DeserializeNotifications,
        false, true, new CancellationToken?(), null);
    }

    private NotificationData DeserializeNotifications(string jsonStr)
    {
      int resultCount = 0;
      jsonStr = VKRequestsDispatcher.GetArrayCountAndRemove(jsonStr, "items", out resultCount);
      jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "profiles", false);
      jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "groups", false);
      NotificationData response =
        JsonConvert.DeserializeObject<GenericRoot<NotificationData>>(jsonStr).response ??
        new NotificationData();
      response.items = response.items ?? new List<Notification>();
      response.profiles = response.profiles ?? new List<User>();
      response.groups = response.groups ?? new List<Group>();
      response.TotalCount = resultCount == 0 ? response.items.Count : resultCount;
      foreach (Notification notification in response.items)
        notification.UpdateNotificationType();
      return response;
    }

    private void AssignKnownNotificationActors(NotificationData data)
    {
      foreach (Notification notification in data.items)
      {
        if (notification.NotType == NotificationType.copy_post ||
            notification.NotType == NotificationType.copy_photo ||
            notification.NotType == NotificationType.copy_video)
          continue;

        User user = notification.ParsedFeedback as User ??
                    notification.ParsedParent as User;
        if (user != null)
        {
          notification.actor_id = user.uid;
          continue;
        }
        Group group = notification.ParsedParent as Group;
        if (group != null)
        {
          notification.actor_id = -group.id;
          continue;
        }
        List<FeedbackUser> feedbackUsers =
          notification.ParsedFeedback as List<FeedbackUser>;
        if (feedbackUsers != null && feedbackUsers.Count > 0)
        {
          notification.actor_id = feedbackUsers[0].owner_id;
          continue;
        }
        Comment comment = notification.ParsedFeedback as Comment;
        if (comment != null && comment.from_id != 0L)
        {
          notification.actor_id = comment.from_id;
          continue;
        }
        WallPost post = notification.ParsedFeedback as WallPost ??
                        notification.ParsedParent as WallPost;
        if (post != null)
          notification.actor_id = post.from_id;
      }
    }

    private bool NeedsResolvedActor(NotificationType type)
    {
      switch (type)
      {
        case NotificationType.copy_post:
        case NotificationType.copy_photo:
        case NotificationType.copy_video:
        case NotificationType.comment_photo:
        case NotificationType.comment_video:
        case NotificationType.wall_publish:
        case NotificationType.new_posts_in_club:
          return true;
        default:
          return false;
      }
    }

    private void ResolveNotificationActors(
      BackendResult<NotificationData, ResultCode> result,
      int pageOffset,
      List<int> indexes,
      int position,
      Action<BackendResult<NotificationData, ResultCode>> callback)
    {
      if (position >= indexes.Count)
      {
        callback(result);
        return;
      }

      int itemIndex = indexes[position];
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        { "count", "1" },
        { "offset", (pageOffset + itemIndex).ToString() },
        { "fields", "sex,photo_50,photo_100,photo_200,photo_max,screen_name" }
      };
      VKRequestsDispatcher.DispatchRequestToVK<NotificationData>(
        "notifications.get",
        parameters,
        (Action<BackendResult<NotificationData, ResultCode>>) (singleResult =>
        {
          if (singleResult.ResultCode == ResultCode.Succeeded &&
              singleResult.ResultData != null)
          {
            User actor = singleResult.ResultData.profiles.FirstOrDefault();
            Group actorGroup = singleResult.ResultData.groups.FirstOrDefault();
            if (actor != null)
            {
              result.ResultData.items[itemIndex].actor_id = actor.uid;
              if (!result.ResultData.profiles.Any(user => user.uid == actor.uid))
                result.ResultData.profiles.Add(actor);
            }
            else if (actorGroup != null)
            {
              result.ResultData.items[itemIndex].actor_id = -actorGroup.id;
              if (!result.ResultData.groups.Any(group => group.id == actorGroup.id))
                result.ResultData.groups.Add(actorGroup);
            }
          }
          this.ResolveNotificationActors(
            result, pageOffset, indexes, position + 1, callback);
        }),
        (Func<string, NotificationData>) this.DeserializeNotifications,
        false, true, new CancellationToken?(), null);
    }

    public void Search(string searchStr, int count, int startTime, int endTime, string startFrom, Action<BackendResult<NewsFeedData, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["q"] = searchStr;
      parameters["count"] = count.ToString();
      if (startTime > 0)
        parameters["start_time"] = startTime.ToString();
      if (endTime > 0)
        parameters["end_time"] = endTime.ToString();
      if (!string.IsNullOrWhiteSpace(startFrom))
        parameters["start_from"] = startFrom;
      parameters["extended"] = "1";
      VKRequestsDispatcher.DispatchRequestToVK<NewsFeedData>("newsfeed.search", parameters, callback, (Func<string, NewsFeedData>) (jsonStr =>
      {
        jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "items", false);
        NewsFeedData response = JsonConvert.DeserializeObject<GenericRoot<NewsFeedData>>(jsonStr).response;
        foreach (NewsItem newsItem in response.items)
        {
          if (newsItem.user != null)
            response.profiles.Add(newsItem.user);
          if (newsItem.group != null)
            response.groups.Add(newsItem.group);
        }
        return response;
      }), false, true, new CancellationToken?(),  null);
    }

    public void MarkAsViewed()
    {
      string methodName = "notifications.markAsViewed";
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      Action<BackendResult<ResponseWithId, ResultCode>> action = (Action<BackendResult<ResponseWithId, ResultCode>>) (r => {});
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type

      Action<BackendResult<ResponseWithId, ResultCode>> callback = (Action<BackendResult<ResponseWithId, ResultCode>>)(res => { });
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>(methodName, parameters, callback, (Func<string, ResponseWithId>) (j => new ResponseWithId()), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void GetBanned(Action<BackendResult<ProfilesAndGroups, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["extended"] = "1";
      parameters["fields"] = "online, online_mobile, photo_max";
      VKRequestsDispatcher.DispatchRequestToVK<ProfilesAndGroups>("newsfeed.getBanned", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void DeleteBan(List<long> uids, List<long> gids, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      NewsFeedService.AddDeleteBan(false, uids, gids, callback);
    }

    public void AddBan(List<long> uids, List<long> gids, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      NewsFeedService.AddDeleteBan(true, uids, gids, callback);
    }

    private static void AddDeleteBan(bool addBan, List<long> uids, List<long> gids, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      if (uids != null && uids.Count > 0)
        parameters["user_ids"] = uids.GetCommaSeparated();
      if (gids != null && gids.Count > 0)
        parameters["group_ids"] = gids.GetCommaSeparated();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>(addBan ? "newsfeed.addBan" : "newsfeed.deleteBan", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void GetSuggestedSources(int offset, int count, bool shuffle, Action<BackendResult<VKList<UserOrGroupSource>, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string index1 = "offset";
      string str1 = offset.ToString();
      parameters[index1] = str1;
      string index2 = "count";
      string str2 = count.ToString();
      parameters[index2] = str2;
      string index3 = "shuffle";
      string str3 = shuffle ? "1" : "0";
      parameters[index3] = str3;
      string index4 = "fields";
      string str4 = "is_member,activity,is_closed,photo_200,photo_max,verified,friends_status,occupation,city,country";
      parameters[index4] = str4;
      VKRequestsDispatcher.DispatchRequestToVK<VKList<UserOrGroupSource>>("newsfeed.getSuggestedSources", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void Unsubscribe(string type, long ownerId, long itemId, Action<BackendResult<bool, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string index1 = "type";
      string str1 = type;
      parameters[index1] = str1;
      string index2 = "owner_id";
      string str2 = ownerId.ToString();
      parameters[index2] = str2;
      string index3 = "item_id";
      string str3 = itemId.ToString();
      parameters[index3] = str3;
      VKRequestsDispatcher.DispatchRequestToVK<bool>("newsfeed.unsubscribe", parameters, callback, (Func<string, bool>) (jsonStr =>
      {
        VKRequestsDispatcher.GenericRoot<int> genericRoot = JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<int>>(jsonStr);
        if (genericRoot == null)
          return false;
        return genericRoot.response == 1;
      }), false, true, new CancellationToken?(),  null);
    }

    public void IgnoreItem(string type, long ownerId, long itemId, Action<BackendResult<bool, ResultCode>> callback)
    {
      this.IgnoreUnignoreItem(true, type, ownerId, itemId, callback);
    }

    public void UnignoreItem(string type, long ownerId, long itemId, Action<BackendResult<bool, ResultCode>> callback)
    {
      this.IgnoreUnignoreItem(false, type, ownerId, itemId, callback);
    }

    private void IgnoreUnignoreItem(bool ignore, string type, long ownerId, long itemId, Action<BackendResult<bool, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      string index1 = "type";
      string str1 = type;
      dictionary[index1] = str1;
      string index2 = "owner_id";
      string str2 = ownerId.ToString();
      dictionary[index2] = str2;
      string index3 = "item_id";
      string str3 = itemId.ToString();
      dictionary[index3] = str3;
      Dictionary<string, string> parameters = dictionary;
      VKRequestsDispatcher.DispatchRequestToVK<bool>(ignore ? "newsfeed.ignoreItem" : "newsfeed.unignoreItem", parameters, callback, (Func<string, bool>) (jsonStr =>
      {
        VKRequestsDispatcher.GenericRoot<int> genericRoot = JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<int>>(jsonStr);
        if (genericRoot == null)
          return false;
        return genericRoot.response == 1;
      }), false, true, new CancellationToken?(),  null);
    }
  }
}
