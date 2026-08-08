using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using VKClient.Audio.Base.BackendServices;
using VKClient.Audio.Base.BLExtensions;
using VKClient.Audio.Base.DataObjects;
using VKClient.Audio.Base.Library;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Library;
using VKClient.Common.Utils;

namespace VKClient.Common.Backend
{
  public class UsersService
  {
    private static readonly Func<string, List<User>> _deserializeUsersFunc = (Func<string, List<User>>) (jsonStr => JsonConvert.DeserializeObject<GenericRoot<VKList<User>>>(jsonStr).response.items);
    private Dictionary<long, User> _usersCache = new Dictionary<long, User>();
    private readonly object _lockObj = new object();
    private int numberOfMinutesUserIsValidInCache = 1440;
    private Dictionary<string, List<Action<BackendResult<List<User>, ResultCode>>>> _callbacksToExecute = new Dictionary<string, List<Action<BackendResult<List<User>, ResultCode>>>>();
    private static UsersService _instance;
    private static ILookup<long, User> _friends;

    public static UsersService Instance
    {
      get
      {
        return UsersService._instance ?? (UsersService._instance = new UsersService());
      }
    }

    private UsersService()
    {
    }

    public User GetCachedUser(long userId)
    {
      lock (this._lockObj)
      {
        if (this._usersCache.ContainsKey(userId))
        {
          User user = this._usersCache[userId];
          if ((DateTime.Now - user.CachedDateTime).TotalMinutes <= (double) this.numberOfMinutesUserIsValidInCache)
            return user;
        }
      }
      return  null;
    }

    public void SetCachedUser(User user)
    {
      user.CachedDateTime = DateTime.Now;
      lock (this._lockObj)
        this._usersCache[user.uid] = user;
    }

    public void SetCachedUsers(IEnumerable<User> users)
    {
      lock (this._lockObj)
      {
        foreach (User user in users)
        {
          user.CachedDateTime = DateTime.Now;
          this._usersCache[user.uid] = user;
        }
      }
    }

    public void GetUsersOnlineStatus(List<int> userIds, Action<BackendResult<List<User>, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<List<User>>("users.get", new Dictionary<string, string>()
      {
        {
          "user_ids",
          userIds.GetCommaSeparated()
        },
        {
          "fields",
          "uid, online,online_mobile"
        }
      }, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void SetStatusText(string text, Action<BackendResult<object, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<object>("status.set", new Dictionary<string, string>()
      {
        {
          "text",
          text
        }
      }, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetUsers(List<long> userIds, Action<BackendResult<List<User>, ResultCode>> callback)
    {
      if (userIds == null || userIds.Count == 0)
      {
        callback(new BackendResult<List<User>, ResultCode>()
        {
          ResultCode = ResultCode.Succeeded,
          ResultData = new List<User>()
        });
        return;
      }

      List<long> requestedIds = userIds
        .Where(uid => uid != 0L && uid > -2000000000L)
        .Distinct()
        .ToList();
      List<User> resultData = new List<User>();
      List<long> missingIds = new List<long>();
      foreach (long userId in requestedIds)
      {
        User cachedUser = this.GetCachedUser(userId);
        if (cachedUser != null)
          resultData.Add(cachedUser);
        else
          missingIds.Add(userId);
      }

      if (missingIds.Count == 0)
      {
        Logger.Instance.Info("Returning {0} users from cache", resultData.Count);
        callback(new BackendResult<List<User>, ResultCode>(ResultCode.Succeeded, resultData));
        return;
      }

      string requestKey = missingIds.OrderBy(id => id).ToList().GetCommaSeparated();
      if (this._callbacksToExecute.ContainsKey(requestKey))
      {
        this._callbacksToExecute[requestKey].Add(callback);
        return;
      }

      this._callbacksToExecute.Add(requestKey, new List<Action<BackendResult<List<User>, ResultCode>>>()
      {
        callback
      });

      List<long> positiveIds = missingIds.Where(uid => uid > 0L).ToList();
      List<long> groupIds = missingIds
        .Where(uid => uid < 0L)
        .Select(uid => -uid)
        .ToList();

      Action loadGroups = null;
      loadGroups = (Action) (() =>
      {
        if (groupIds.Count == 0)
        {
          this.CompleteGetUsersRequest(requestKey, ResultCode.Succeeded, resultData);
          return;
        }

        VKRequestsDispatcher.DispatchRequestToVK<List<Group>>(
          "groups.getById",
          new Dictionary<string, string>()
          {
            { "group_ids", groupIds.GetCommaSeparated() },
            { "fields", "photo_50,photo_100,photo_200,photo_max,is_messages_blocked" }
          },
          (Action<BackendResult<List<Group>, ResultCode>>) (groupsResult =>
          {
            if (groupsResult.ResultCode != ResultCode.Succeeded)
            {
              this.CompleteGetUsersRequest(requestKey, groupsResult.ResultCode, null);
              return;
            }

            foreach (Group group in groupsResult.ResultData ?? new List<Group>())
            {
              User groupUser = new User();
              groupUser.InitFromGroup(group);
              resultData.Add(groupUser);
            }
            this.CompleteGetUsersRequest(requestKey, ResultCode.Succeeded, resultData);
          }),
          (Func<string, List<Group>>) (jsonStr =>
            JsonConvert.DeserializeObject<GenericRoot<List<Group>>>(jsonStr).response ?? new List<Group>()),
          false,
          true,
          new CancellationToken?(),
          null);
      });

      if (positiveIds.Count == 0)
      {
        loadGroups();
        return;
      }

      VKRequestsDispatcher.DispatchRequestToVK<List<User>>(
        "users.get",
        new Dictionary<string, string>()
        {
          { "user_ids", positiveIds.GetCommaSeparated() },
          { "fields", "first_name,last_name,online,online_mobile,photo_50,photo_100,photo_200,photo_max,sex,friend_status,first_name_dat,last_name_dat" }
        },
        (Action<BackendResult<List<User>, ResultCode>>) (usersResult =>
        {
          if (usersResult.ResultCode != ResultCode.Succeeded)
          {
            this.CompleteGetUsersRequest(requestKey, usersResult.ResultCode, null);
            return;
          }

          resultData.AddRange(usersResult.ResultData ?? new List<User>());
          loadGroups();
        }),
        (Func<string, List<User>>) (jsonStr =>
          JsonConvert.DeserializeObject<GenericRoot<List<User>>>(jsonStr).response ?? new List<User>()),
        false,
        true,
        new CancellationToken?(),
        null);
    }

    private void CompleteGetUsersRequest(string requestKey, ResultCode resultCode, List<User> users)
    {
      if (resultCode == ResultCode.Succeeded && users != null)
        this.SetCachedUsers(users);

      BackendResult<List<User>, ResultCode> result =
        new BackendResult<List<User>, ResultCode>(resultCode, users);
      List<Action<BackendResult<List<User>, ResultCode>>> callbacks;
      if (!this._callbacksToExecute.TryGetValue(requestKey, out callbacks))
        return;

      this._callbacksToExecute.Remove(requestKey);
      foreach (Action<BackendResult<List<User>, ResultCode>> callback in callbacks)
        callback(result);
    }

    public void GetUsersNoCache(List<int> userIds, Action<BackendResult<List<User>, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<List<User>>("users.get", new Dictionary<string, string>()
      {
        {
          "user_ids",
          userIds.GetCommaSeparated()
        },
        {
          "fields",
          "uid,first_name,last_name,online,online_mobile,photo_max,sex,photo_medium,friend_status"
        }
      }, (Action<BackendResult<List<User>, ResultCode>>) (res =>
      {
        if (res.ResultCode == ResultCode.Succeeded)
        {
          foreach (User user in res.ResultData)
          {
            user.CachedDateTime = DateTime.Now;
            this._usersCache[user.uid] = user;
          }
        }
        callback(res);
      }),  null, false, true, new CancellationToken?(),  null);
    }

    public void GetFriendsForCurrentUser(Action<BackendResult<List<User>, ResultCode>> callback)
    {
      Random random = new Random(DateTime.Now.Millisecond);
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary.Add("random", random.Next().ToString());
      dictionary.Add("fields", "uid,first_name,last_name,online,online_mobile,photo_max,site,contacts,bdate,photo_max,bdate,sex,first_name_dat");
      dictionary.Add("order", "hints");
      dictionary.Add("count", "5000");
      string methodName = "friends.get";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<List<User>, ResultCode>> callback1 = (Action<BackendResult<List<User>, ResultCode>>) (res =>
      {
        if (res.ResultCode == ResultCode.Succeeded)
          UsersService._friends = res.ResultData.ToLookup<User, long>((Func<User, long>) (u => u.uid));
        callback(res);
      });
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<List<User>>(methodName, parameters, callback1, (Func<string, List<User>>) (jsonStr => JsonConvert.DeserializeObject<GenericRoot<VKList<User>>>(jsonStr).response.items), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void GetFriendsWithRequests(Action<BackendResult<AllFriendsList, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        {
          "fields",
          "uid,first_name,last_name,online,online_mobile,photo_max,photo_100,photo_200,sex,friend_status"
        },
        {
          "count",
          "5000"
        }
      };

      VKRequestsDispatcher.DispatchRequestToVK<List<User>>("friends.get", parameters,
        (Action<BackendResult<List<User>, ResultCode>>) (friendsResult =>
        {
          if (friendsResult.ResultCode != ResultCode.Succeeded)
          {
            callback(new BackendResult<AllFriendsList, ResultCode>(
              friendsResult.ResultCode));
            return;
          }

          this.GetFriendRequests(false, 0, 100,
            (Action<BackendResult<FriendRequests, ResultCode>>) (requestsResult =>
            {
              callback(new BackendResult<AllFriendsList, ResultCode>(
                ResultCode.Succeeded,
                new AllFriendsList()
                {
                  friends = friendsResult.ResultData ?? new List<User>(),
                  requests = requestsResult.ResultCode == ResultCode.Succeeded
                    ? requestsResult.ResultData
                    : new FriendRequests()
                }));
            }));
        }),
        (Func<string, List<User>>) (jsonString =>
        {
          VKList<User> response =
            JsonConvert.DeserializeObject<GenericRoot<VKList<User>>>(jsonString).response;
          return response != null && response.items != null
            ? response.items
            : new List<User>();
        }), false, true, new CancellationToken?(), null);
    }

    public void GetFriends(long uid, long lid, Action<BackendResult<List<User>, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters.Add("fields", "uid,first_name,last_name,online, online_mobile,photo_max");
      parameters.Add("order", "hints");
      parameters.Add("count", "5000");
      if (uid != 0L)
        parameters["user_id"] = uid.ToString();
      if (lid != 0L)
        parameters["list_id"] = lid.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<List<User>>("friends.get", parameters, callback, UsersService._deserializeUsersFunc, false, true, new CancellationToken?(),  null);
    }

    public void GetStatus(long userId, Action<BackendResult<UserStatus, ResultCode>> callback)
    {
      if (userId < 0L)
        callback(new BackendResult<UserStatus, ResultCode>(ResultCode.Succeeded, new UserStatus()));
      else
        VKRequestsDispatcher.DispatchRequestToVK<UserStatus>(
          "users.get",
          new Dictionary<string, string>()
          {
            { "user_ids", userId.ToString() },
            { "fields", "online,last_seen" }
          },
          callback,
          (Func<string, UserStatus>) (jsonStr =>
          {
            List<User> users =
              JsonConvert.DeserializeObject<GenericRoot<List<User>>>(jsonStr).response;
            User user = users != null ? users.FirstOrDefault() : null;
            return user != null && user.last_seen != null
              ? new UserStatus()
              {
                online = user.online,
                time = user.last_seen.time,
                platform = user.last_seen.platform
              }
              : new UserStatus();
          }),
          false,
          true,
          new CancellationToken?(),
          null);
    }

    public void AddFriend(long userId, Action<BackendResult<OwnCounters, ResultCode>> callback)
    {
      this.FriendAddDelete(userId, true, callback);
    }

    public void DeleteFriend(long userId, Action<BackendResult<OwnCounters, ResultCode>> callback)
    {
      this.FriendAddDelete(userId, false, callback);
    }

    public void SearchUsers(string searchString, int offset, int count, Action<BackendResult<VKList<User>, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<VKList<User>>("users.search", new Dictionary<string, string>()
      {
        {
          "q",
          searchString
        },
        {
          "offset",
          offset.ToString()
        },
        {
          "count",
          count.ToString()
        },
        {
          "fields",
          "screen_name,online,last_seen,photo_50,photo_100,photo_200,photo_max,city,country,friend_status"
        }
      }, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void SaveProfilePhoto(Rect thumbnailRect, byte[] photoData, Action<BackendResult<ProfilePhoto, ResultCode>> callback)
    {
      this.GetPhotoUploadServer((Action<BackendResult<UploadServerAddress, ResultCode>>) (res =>
      {
        if (res.ResultCode != ResultCode.Succeeded)
        {
          callback(new BackendResult<ProfilePhoto, ResultCode>(res.ResultCode));
        }
        else
        {
          string uploadUrl = res.ResultData.upload_url;
          // ISSUE: explicit reference operation
          if (((Rect) @thumbnailRect).Width != 0.0)
          {
            // ISSUE: explicit reference operation
            // ISSUE: explicit reference operation
            // ISSUE: explicit reference operation
            string str = string.Format("&_square_crop={0},{1},{2}&_full={0},{1},{2},{2}", (int) ((Rect) @thumbnailRect).X, (int) ((Rect) @thumbnailRect).Y, (int) ((Rect) @thumbnailRect).Width);
            uploadUrl += str;
          }
          MemoryStream memoryStream = new MemoryStream(photoData);
          JsonWebRequest.Upload(uploadUrl, (Stream) memoryStream, "photo", "image", (Action<JsonResponseData>) (jsonResult =>
          {
            if (!jsonResult.IsSucceeded)
              callback(new BackendResult<ProfilePhoto, ResultCode>(ResultCode.UnknownError));
            else
              this.SaveProfilePhoto(JsonConvert.DeserializeObject<UploadPhotoResponseData>(jsonResult.JsonString), callback);
          }), "MyImage.jpg",  null,  null);
        }
      }));
    }

    public void UploadProfilePhoto(byte[] photoData, Action<BackendResult<UploadPhotoResponseData, ResultCode>> callback)
    {
      this.GetPhotoUploadServer((Action<BackendResult<UploadServerAddress, ResultCode>>) (res =>
      {
        if (res.ResultCode != ResultCode.Succeeded)
          callback(new BackendResult<UploadPhotoResponseData, ResultCode>(res.ResultCode));
        else
          JsonWebRequest.Upload(res.ResultData.upload_url, (Stream) new MemoryStream(photoData), "photo", "image", (Action<JsonResponseData>) (jsonResult =>
          {
            if (!jsonResult.IsSucceeded)
              callback(new BackendResult<UploadPhotoResponseData, ResultCode>(ResultCode.UnknownError));
            else
              callback(new BackendResult<UploadPhotoResponseData, ResultCode>(ResultCode.Succeeded, JsonConvert.DeserializeObject<UploadPhotoResponseData>(jsonResult.JsonString)));
          }), "MyImage.jpg",  null,  null);
      }));
    }

    public void SaveProfilePhoto(UploadPhotoResponseData responseData, Action<BackendResult<ProfilePhoto, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary["server"] = responseData.server;
      dictionary["photo"] = responseData.photo;
      dictionary["hash"] = responseData.hash;
      string methodName = "photos.saveOwnerPhoto";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<ProfilePhoto, ResultCode>> callback1 = callback;
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<ProfilePhoto>(methodName, parameters, callback1, (Func<string, ProfilePhoto>) (jsonStr => JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<ProfilePhoto>>(jsonStr).response), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void GetPhotoUploadServer(Action<BackendResult<UploadServerAddress, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<UploadServerAddress>("photos.getOwnerPhotoUploadServer", new Dictionary<string, string>(), callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetProfileInfo(long userId, Action<BackendResult<UserData, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        { "user_ids", userId.ToString() },
        { "fields", "photo_max,photo_50,photo_100,photo_200,online,last_seen,sex,screen_name,status,activity,bdate,city,country,home_town,interests,music,movies,tv,books,games,quotes,about,site,contacts,relation,relatives,counters,friend_status,can_post,can_write_private_message,is_favorite,is_subscribed,verified,wall_default,occupation,career,education" }
      };

      VKRequestsDispatcher.DispatchRequestToVK<UserData>("users.get", parameters, (Action<BackendResult<UserData, ResultCode>>) (profileResult =>
      {
        if (profileResult.ResultCode != ResultCode.Succeeded || profileResult.ResultData == null)
        {
          callback(profileResult);
          return;
        }

        UserData profileData = profileResult.ResultData;
        Action loadGiftPreviews = (Action) (() =>
          GiftsService.Instance.Get(userId, 3, 0, (Action<BackendResult<GiftsResponse, ResultCode>>) (giftsResult =>
          {
            if (giftsResult.ResultCode == ResultCode.Succeeded &&
                giftsResult.ResultData != null && giftsResult.ResultData.gifts != null)
            {
              profileData.gifts = giftsResult.ResultData.gifts;
              if (profileData.user != null && profileData.user.counters != null &&
                  profileData.user.counters.gifts == 0)
                profileData.user.counters.gifts = profileData.gifts.count;
            }
            callback(profileResult);
          })));

        this.GetSubscriptions(userId, (Action<BackendResult<UsersAndGroups, ResultCode>>) (subscriptionsResult =>
        {
          if (subscriptionsResult.ResultCode == ResultCode.Succeeded &&
              subscriptionsResult.ResultData != null)
          {
            List<Group> groups = subscriptionsResult.ResultData.groups ?? new List<Group>();
            List<User> users = subscriptionsResult.ResultData.users ?? new List<User>();
            profileData.subscriptions = new VKList<SubscriptionObj>()
            {
              count = groups.Count + users.Count,
              items = groups.Select(group => new SubscriptionObj()
              {
                type = "group",
                id = group.id,
                name = group.name,
                photo_max = group.photo_max ?? group.photo_200 ?? group.photo_100 ?? group.photo_50
              }).Concat(users.Select(user => new SubscriptionObj()
              {
                type = "profile",
                id = user.id,
                first_name = user.first_name,
                last_name = user.last_name,
                name = user.Name,
                photo_max = user.photo_max ?? user.photo_200 ?? user.photo_100 ?? user.photo_50
              })).ToList()
            };
            if (profileData.user != null && profileData.user.counters != null)
            {
              if (profileData.user.counters.groups == 0)
                profileData.user.counters.groups = groups.Count;
              if (profileData.user.counters.subscriptions == 0)
                profileData.user.counters.subscriptions = users.Count;
            }
          }
          loadGiftPreviews();
        }));
      }), (Func<string, UserData>) (jsonStr =>
      {
        List<User> users = JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<List<User>>>(jsonStr).response;
        User user = users != null && users.Count > 0 ? users[0] : new User();
        if (user.counters == null)
          user.counters = new Counters();
        if (string.IsNullOrEmpty(user.activity))
          user.activity = user.status;

        return new UserData()
        {
          user = user,
          city = user.city,
          full_gen = user,
          friend = new FriendStatus() { uid = user.id, friend_status = (FriendshipStatus) user.friend_status },
          randomMutualFriends = new List<User>(),
          relatives = new List<User>(),
          followers = new VKList<User>() { items = new List<User>() },
          subscriptions = new VKList<SubscriptionObj>() { items = new List<SubscriptionObj>() },
          gifts = new VKList<GiftItemData>() { items = new List<GiftItemData>() },
          photos = new VKList<Photo>() { items = new List<Photo>() },
          videos = new List<Video>(),
          products = new List<Product>(),
          audios = new List<AudioObj>(),
          topics = new List<Topic>(),
          areFriendsStatus = user.friend_status
        };
      }), false, true, new CancellationToken?(), null);
    }

    public void FriendAddDelete(long userId, bool add, Action<BackendResult<OwnCounters, ResultCode>> callback)
    {
      string method = add ? "friends.add" : "friends.delete";
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>(method,
        new Dictionary<string, string>() { { "user_id", userId.ToString() } },
        result =>
        {
          if (result.ResultCode != ResultCode.Succeeded)
          {
            callback(new BackendResult<OwnCounters, ResultCode>(result.ResultCode));
            return;
          }
          // OpenVK does not return counters from friends.add/delete. Fetch
          // them separately instead of relying on the legacy execute script.
          AccountService.Instance.GetCounters(counters =>
          {
            if (counters.ResultCode == ResultCode.Succeeded)
              callback(counters);
            else
              callback(new BackendResult<OwnCounters, ResultCode>(ResultCode.Succeeded, new OwnCounters()));
          });
        }, jsonStr => new ResponseWithId(), false, true, new CancellationToken?(), null);
    }

    public void GetFriendsAndMutual(long userId, Action<BackendResult<FriendsAndMutualFriends, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["user_id"] = userId.ToString();
      parameters["fields"] = "uid,first_name,last_name,online,online_mobile,photo_max";
      parameters["count"] = "5000";
      VKRequestsDispatcher.DispatchRequestToVK<FriendsAndMutualFriends>("friends.get", parameters, callback,
        (Func<string, FriendsAndMutualFriends>) (jsonStr =>
        {
          VKList<User> response = JsonConvert.DeserializeObject<GenericRoot<VKList<User>>>(jsonStr).response;
          return new FriendsAndMutualFriends()
          {
            friends = response != null && response.items != null ? response.items : new List<User>(),
            mutualFriends = new List<long>()
          };
        }), false, true, new CancellationToken?(), null);
    }

    public void GetFriendsAndLists(Action<BackendResult<FriendsAndLists, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string str = "var friends=API.friends.get({ \"order\":\"hints\", \"fields\":\"uid,first_name,last_name,online, online_mobile,photo_max, first_name_gen, last_name_gen\"}).items;\r\nvar friendLists = API.friends.getLists({\"return_system\":1}).items;\r\n\r\nreturn {\"friends\":friends, \"friendLists\":friendLists};";
      parameters["code"] = str;
      VKRequestsDispatcher.DispatchRequestToVK<FriendsAndLists>("execute", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void AddList(string name, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["name"] = name;
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("friends.addList", parameters, callback, (Func<string, ResponseWithId>) (jsonStr =>
      {
        VKRequestsDispatcher.GenericRoot<FriendsList> genericRoot = JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<FriendsList>>(jsonStr);
        return new ResponseWithId()
        {
          response = genericRoot.response.lid
        };
      }), false, true, new CancellationToken?(),  null);
    }

    public void RemoveList(long lid, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["list_id"] = lid.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("friends.deleteList", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void EditList(long lid, string name, List<long> uids, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["name"] = name;
      parameters["list_id"] = lid.ToString();
      if (!uids.IsNullOrEmpty())
        parameters["user_ids"] = uids.GetCommaSeparated();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("friends.editList", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void GetFriendsForList(long lid, Action<BackendResult<List<User>, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["list_id"] = lid.ToString();
      parameters["fields"] = "uid,first_name,last_name,online, online_mobile,photo_max";
      parameters["count"] = "5000";
      VKRequestsDispatcher.DispatchRequestToVK<List<User>>("friends.get", parameters, callback, UsersService._deserializeUsersFunc, false, true, new CancellationToken?(),  null);
    }

    public void EditListName(long lid, string updatedName, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      this.GetFriendsForList(lid, (Action<BackendResult<List<User>, ResultCode>>) (resFriends =>
      {
        if (resFriends.ResultCode != ResultCode.Succeeded)
          callback(new BackendResult<ResponseWithId, ResultCode>(resFriends.ResultCode));
        else
          this.EditList(lid, updatedName, resFriends.ResultData.Select<User, long>((Func<User, long>) (u => u.uid)).ToList<long>(), callback);
      }));
    }

    public void GetFollowers(long uid, int offset, Action<BackendResult<List<User>, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary["user_id"] = uid.ToString();
      dictionary["offset"] = offset.ToString();
      dictionary["fields"] = "online, online_mobile,photo_max";
      string methodName = "users.getFollowers";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<List<User>, ResultCode>> callback1 = callback;
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<List<User>>(methodName, parameters, callback1, (Func<string, List<User>>) (jsonStr => JsonConvert.DeserializeObject<GenericRoot<VKList<User>>>(jsonStr).response.items), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void GetFriendRequests(bool areSuggestedFriends, int offset, int count, Action<BackendResult<FriendRequests, ResultCode>> callback)
    {
      if (areSuggestedFriends)
      {
        callback(new BackendResult<FriendRequests, ResultCode>(
          ResultCode.Succeeded,
          new FriendRequests() { are_suggested_friends = true }));
        return;
      }

      Dictionary<string, string> dictionary = new Dictionary<string, string>()
      {
        {
          "count",
          count.ToString()
        },
        {
          "offset",
          offset.ToString()
        },
        {
          "fields",
          "uid,first_name,last_name,online,online_mobile,photo_50,photo_100,photo_200,photo_max,sex,friend_status"
        }
      };
      string methodName = "friends.getRequests";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<FriendRequests, ResultCode>> callback1 = callback;
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<FriendRequests>(methodName, parameters, callback1,
        (Func<string, FriendRequests>) (jsonStr =>
        {
          VKList<User> response =
            JsonConvert.DeserializeObject<GenericRoot<VKList<User>>>(jsonStr).response;
          List<User> profiles = response != null && response.items != null
            ? response.items
            : new List<User>();
          int total = response != null ? response.count : profiles.Count;
          return new FriendRequests()
          {
            count = total,
            menu_counter = total,
            profiles = profiles,
            requests = profiles.Select(user => new FriendRequest()
            {
              user_id = user.uid
            }).ToList(),
            are_suggested_friends = false
          };
        }), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void GetLists(Action<BackendResult<List<FriendsList>, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary["return_system"] = "1";
      string methodName = "friends.getLists";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<List<FriendsList>, ResultCode>> callback1 = callback;
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<List<FriendsList>>(methodName, parameters, callback1, (Func<string, List<FriendsList>>) (jsonStr => JsonConvert.DeserializeObject<GenericRoot<VKList<FriendsList>>>(jsonStr).response.items), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void GetProfileBasicInfo(long userId, Action<BackendResult<UserData, ResultCode>> callback)
    {
      this.GetProfileInfo(userId, callback);
    }

    public void GetSubscriptions(long uid, Action<BackendResult<UsersAndGroups, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<UsersAndGroups>("groups.get", new Dictionary<string, string>()
      {
        { "user_id", uid.ToString() },
        { "count", "5000" },
        { "offset", "0" },
        { "extended", "1" },
        { "fields", "photo_50,photo_100,photo_200,screen_name,is_member" }
      }, (Action<BackendResult<UsersAndGroups, ResultCode>>) (groupsResult =>
      {
        if (groupsResult.ResultCode != ResultCode.Succeeded ||
            groupsResult.ResultData == null ||
            uid != AppGlobalStateManager.Current.LoggedInUserId)
        {
          callback(groupsResult);
          return;
        }

        // OpenVK exposes outgoing subscriptions to people through
        // friends.getRequests(out=1). Its count is currently incorrect, so
        // the item list is authoritative.
        VKRequestsDispatcher.DispatchRequestToVK<VKList<User>>(
          "friends.getRequests",
          new Dictionary<string, string>()
          {
            { "out", "1" },
            { "count", "100" },
            { "offset", "0" },
            { "fields", "photo_50,photo_100,photo_200,photo_max,screen_name,online" }
          },
          (Action<BackendResult<VKList<User>, ResultCode>>) (usersResult =>
          {
            if (usersResult.ResultCode == ResultCode.Succeeded &&
                usersResult.ResultData != null)
              groupsResult.ResultData.users =
                usersResult.ResultData.items ?? new List<User>();
            callback(groupsResult);
          }),
          (Func<string, VKList<User>>) (jsonStr =>
          {
            VKList<User> response =
              JsonConvert.DeserializeObject<GenericRoot<VKList<User>>>(jsonStr).response;
            return response ?? new VKList<User>();
          }),
          false,
          true,
          new CancellationToken?(),
          null);
      }), (Func<string, UsersAndGroups>) (jsonStr =>
      {
        VKList<Group> response = JsonConvert.DeserializeObject<GenericRoot<VKList<Group>>>(jsonStr).response;
        return new UsersAndGroups()
        {
          groups = response != null && response.items != null ? response.items : new List<Group>()
        };
      }), false, true, new CancellationToken?(), null);
    }

    public void GetUsersForTile(long uid, Action<BackendResult<List<User>, ResultCode>> callback)
    {
      if (uid < 0L)
      {
        List<long> userIds = new List<long>();
        userIds.Add(uid);
        Action<BackendResult<List<User>, ResultCode>> callback1 = callback;
        this.GetUsers(userIds, callback1);
      }
      else
      {
        Dictionary<string, string> parameters = new Dictionary<string, string>();
        parameters["user_ids"] = uid.ToString();
        parameters["fields"] = "photo_max";
        string name = Thread.CurrentThread.CurrentUICulture.Name;
        parameters["name_case"] = "ins";
        VKRequestsDispatcher.DispatchRequestToVK<List<User>>("users.get", parameters, callback,  null, false, true, new CancellationToken?(),  null);
      }
    }

    public void Search(SearchParams searchParams, string query, int offset, int count, Action<BackendResult<VKList<User>, ResultCode>> callback)
    {
      if (searchParams == null)
        return;
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        { "q", query ?? string.Empty },
        { "offset", offset.ToString() },
        { "count", count.ToString() }
      };

      // OpenVK users.search currently supports city, sex, status and online from
      // the legacy WP filter set. country/age/has_photo are not method arguments.
      City city = searchParams.GetValue<City>("city");
      if (city != null && !string.IsNullOrWhiteSpace(city.title))
        parameters["city"] = city.title;

      int sex = searchParams.GetValue<int>("sex");
      if (sex > 0)
        parameters["sex"] = sex.ToString();

      int status = searchParams.GetValue<int>("status");
      if (status > 0)
        parameters["status"] = status.ToString();

      if (searchParams.GetValue<bool>("online"))
        parameters["online"] = "1";

      parameters["fields"] = "photo_50,photo_100,photo_200,photo_max,verified,city,friend_status,online,last_seen";
      VKRequestsDispatcher.DispatchRequestToVK<VKList<User>>("users.search", parameters, callback, null, false, true, new CancellationToken?(), null);
    }

    public void GetNearby(double lat, double lon, uint? accuracy, Action<BackendResult<VKList<User>, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        {
          "latitude",
          lat.ToString((IFormatProvider) CultureInfo.InvariantCulture)
        },
        {
          "longitude",
          lon.ToString((IFormatProvider) CultureInfo.InvariantCulture)
        },
        {
          "radius",
          "2"
        },
        {
          "timeout",
          "300"
        },
        {
          "fields",
          "photo_max,verified,occupation,city,country,friend_status,common_count"
        }
      };
      if (accuracy.HasValue)
        parameters["accuracy"] = accuracy.Value.ToString((IFormatProvider) CultureInfo.InvariantCulture);
      VKRequestsDispatcher.DispatchRequestToVK<VKList<User>>("users.getNearby", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetFriendsSuggestions(string nextFrom, int count, Action<BackendResult<VKList<User>, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<VKList<User>>("friends.getRecommendations", new Dictionary<string, string>()
      {
        {
          "count",
          count.ToString()
        },
        {
          "start_from",
          nextFrom
        },
        {
          "fields",
          "photo_max,friend_status"
        }
      }, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void GetFeatureUsers(int count, Action<BackendResult<VKList<User>, ResultCode>> callback)
    {
      VKRequestsDispatcher.DispatchRequestToVK<VKList<User>>("friends.getFeatureUsers", new Dictionary<string, string>()
      {
        {
          "count",
          count.ToString()
        },
        {
          "fields",
          "photo_max"
        },
        {
          "feature",
          "contacts_import"
        }
      }, callback,  null, false, true, new CancellationToken?(),  null);
    }
  }
}
