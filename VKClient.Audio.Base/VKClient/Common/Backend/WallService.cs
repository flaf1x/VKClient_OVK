using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Utils;

namespace VKClient.Common.Backend
{
  public class WallService
  {
    private static WallService _current = new WallService();

    public static WallService Current
    {
      get
      {
        return WallService._current;
      }
    }

    public void GetWallSubscriptionsProfiles(int offset, int count, Action<BackendResult<VKList<User>, ResultCode>> callback)
    {
      VKRequestsDispatcher.Execute<VKList<User>>(string.Format("var subscriptions = API.wall.getSubscriptions({{\"offset\":{0}, \"count\":{1}, \"type\":1}});\r\nvar users = [];\r\nif (subscriptions.items.length > 0)\r\n{{\r\n     users = API.users.get({{\"user_ids\":subscriptions.items, \"fields\":\"photo_max, online, online_mobile\"}});\r\n}}\r\nreturn {{\"items\": users, \"count\":subscriptions.count}};", offset, count), callback,  null, false, true, new CancellationToken?());
    }

    public void GetWallSubscriptionsGroups(int offset, int count, Action<BackendResult<VKList<Group>, ResultCode>> callback)
    {
      VKRequestsDispatcher.Execute<VKList<Group>>(string.Format("var subscriptions = API.wall.getSubscriptions({{\"offset\":{0}, \"count\":{1}, \"type\":2}});\r\nvar groups = [];\r\nif (subscriptions.items.length > 0)\r\n{{\r\n   var groupIds = [];\r\n   var i= 0;\r\n\r\n   while (i < subscriptions.items.length)\r\n   {{\r\n       groupIds.push(-subscriptions.items[i]);\r\n       i = i + 1;\r\n   }}\r\n\r\n   groups = API.groups.getById({{\"group_ids\":groupIds}});\r\n}}\r\nreturn {{\"items\": groups, \"count\":subscriptions.count}};", offset, count), callback,  null, false, true, new CancellationToken?());
    }

    public void WallSubscriptionsUnsubscribe(List<long> ownerIds, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      string format = "API.wall.unsubscribe({{\"owner_id\":{0} }});";
      string code = "";
      foreach (long ownerId in ownerIds)
        code = code + string.Format(format, ownerId) + Environment.NewLine;
      VKRequestsDispatcher.Execute<ResponseWithId>(code, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?());
    }

    public void WallSubscriptionsSubscribe(long ownerId, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["owner_id"] = ownerId.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("wall.subscribe", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void PinUnpin(bool pin, long ownerId, long postId, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["owner_id"] = ownerId.ToString();
      parameters["post_id"] = postId.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>(pin ? "wall.pin" : "wall.unpin", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void Edit(WallPostRequestData postData, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      long num;
      // owner_id is optional in old VK, but required by OpenVK. A zero value
      // in the editor means the logged-in user's wall.
      parameters["owner_id"] = (postData.owner_id != 0L
        ? postData.owner_id
        : AppGlobalStateManager.Current.LoggedInUserId).ToString();
      Dictionary<string, string> dictionary1 = parameters;
      string index1 = "post_id";
      num = postData.post_id;
      string str1 = num.ToString();
      dictionary1[index1] = str1;
      if (!string.IsNullOrEmpty(postData.message))
        parameters["message"] = postData.message ?? "";
      parameters["signed"] = postData.Sign ? "1" : "0";
      if (!postData.AttachmentIds.IsNullOrEmpty())
        parameters["attachments"] = postData.AttachmentIds.GetCommaSeparated(",");
      double? nullable;
      if (postData.latitude.HasValue)
      {
        Dictionary<string, string> dictionary2 = parameters;
        string index2 = "lat";
        nullable = postData.latitude;
        string str2 = nullable.Value.ToString((IFormatProvider) CultureInfo.InvariantCulture);
        dictionary2[index2] = str2;
      }
      nullable = postData.longitude;
      if (nullable.HasValue)
      {
        Dictionary<string, string> dictionary2 = parameters;
        string index2 = "long";
        nullable = postData.longitude;
        string str2 = nullable.Value.ToString((IFormatProvider) CultureInfo.InvariantCulture);
        dictionary2[index2] = str2;
      }
      if (postData.publish_date.HasValue)
      {
        Dictionary<string, string> dictionary2 = parameters;
        string index2 = "publish_date";
        num = postData.publish_date.Value;
        string str2 = num.ToString((IFormatProvider) CultureInfo.InvariantCulture);
        dictionary2[index2] = str2;
      }
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("wall.edit", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()
      {
        response = 1L
      }), false, true, new CancellationToken?(),  null);
    }

    public void Post(WallPostRequestData postData, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      long num;
      if (postData.owner_id != 0L)
      {
        Dictionary<string, string> dictionary = parameters;
        string index = "owner_id";
        num = postData.owner_id;
        string str = num.ToString();
        dictionary[index] = str;
      }
      if (!string.IsNullOrEmpty(postData.message))
        parameters["message"] = postData.message ?? "";
      if (!postData.AttachmentIds.IsNullOrEmpty())
        parameters["attachments"] = postData.AttachmentIds.GetCommaSeparated(",");
      double? nullable;
      if (postData.latitude.HasValue)
      {
        Dictionary<string, string> dictionary = parameters;
        string index = "lat";
        nullable = postData.latitude;
        string str = nullable.Value.ToString((IFormatProvider) CultureInfo.InvariantCulture);
        dictionary[index] = str;
      }
      nullable = postData.longitude;
      if (nullable.HasValue)
      {
        Dictionary<string, string> dictionary = parameters;
        string index = "long";
        nullable = postData.longitude;
        string str = nullable.Value.ToString((IFormatProvider) CultureInfo.InvariantCulture);
        dictionary[index] = str;
      }
      if (postData.post_id != 0L)
      {
        Dictionary<string, string> dictionary = parameters;
        string index = "post_id";
        num = postData.post_id;
        string str = num.ToString();
        dictionary[index] = str;
      }
      parameters["from_group"] = postData.OnBehalfOfGroup || postData.Sign ? "1" : "0";
      parameters["signed"] = postData.Sign ? "1" : "0";
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("wall.post", parameters, callback, (Func<string, ResponseWithId>) (jsonStr =>
      {
        WallService.ResponseWithPostId responseWithPostId = JsonConvert.DeserializeObject<WallService.ResponseWithPostId>(jsonStr);
        return new ResponseWithId()
        {
          response = responseWithPostId.response.post_id
        };
      }), false, true, new CancellationToken?(),  null);
    }

    public void Report(long ownerId, long id, ReportReason reportReason, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["owner_id"] = ownerId.ToString();
      parameters["post_id"] = id.ToString();
      parameters["reason"] = ((int) reportReason).ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("wall.reportPost", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void ReportComment(long ownerId, long commentId, ReportReason reportReason, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["owner_id"] = ownerId.ToString();
      parameters["comment_id"] = commentId.ToString();
      parameters["reason"] = ((int) reportReason).ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("wall.reportComment", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void GetWallPostByIdWithComments(long postId, long ownerId, int offset, int countToRead, int knownTotalCount, bool needWallPost, Action<BackendResult<GetWallPostResponseData, ResultCode>> callback, long pollId = 0, long pollOwnerId = 0, LikeObjectType likeObjType = LikeObjectType.post, WallPost knownWallPost = null)
    {
      if (!needWallPost)
      {
        this.LoadWallComments(
          postId,
          ownerId,
          offset,
          countToRead,
          knownWallPost ?? new WallPost(),
          new List<User>(),
          new List<Group>(),
          callback);
        return;
      }

      this.GetWallById(ownerId, postId, (Action<BackendResult<WallData, ResultCode>>) (wallResult =>
      {
        if (wallResult.ResultCode != ResultCode.Succeeded)
        {
          this.LoadWallComments(
            postId,
            ownerId,
            offset,
            countToRead,
            new WallPost()
            {
              id = postId,
              to_id = ownerId,
              from_id = ownerId,
              post_type = "post"
            },
            new List<User>(),
            new List<Group>(),
            callback);
          return;
        }

        WallData wallData = wallResult.ResultData ?? new WallData();
        WallPost wallPost = wallData.items != null
          ? wallData.items.FirstOrDefault()
          : null;
        this.LoadWallComments(
          postId,
          ownerId,
          offset,
          countToRead,
          wallPost ?? new WallPost(),
          wallData.profiles ?? new List<User>(),
          wallData.groups ?? new List<Group>(),
          callback);
      }));
    }

    private void LoadWallComments(
      long postId,
      long ownerId,
      int offset,
      int countToRead,
      WallPost wallPost,
      List<User> wallUsers,
      List<Group> wallGroups,
      Action<BackendResult<GetWallPostResponseData, ResultCode>> callback)
    {
      // OpenVK's wall.getComments passes offset + 1 to Nette's page()
      // method, so offset is a zero-based page number rather than a row
      // offset as it was in VK API. Callers use a fixed page size.
      int apiOffset = countToRead > 0 ? offset / countToRead : 0;
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        { "post_id", postId.ToString() },
        { "owner_id", ownerId.ToString() },
        { "offset", apiOffset.ToString() },
        { "count", countToRead.ToString() },
        { "need_likes", "1" },
        { "sort", "desc" },
        { "extended", "1" },
        { "fields", "sex,screen_name,photo_50,photo_100,photo_200,photo_max,online,online_mobile,first_name_dat,last_name_dat" }
      };

      VKRequestsDispatcher.DispatchRequestToVK<CommentsResponse>(
        "wall.getComments",
        parameters,
        (Action<BackendResult<CommentsResponse, ResultCode>>) (commentsResult =>
        {
          if (commentsResult.ResultCode != ResultCode.Succeeded)
          {
            WallPost fallbackWallPost = wallPost ?? new WallPost();
            fallbackWallPost.comments = fallbackWallPost.comments ?? new Comments();
            fallbackWallPost.likes = fallbackWallPost.likes ?? new Likes();
            fallbackWallPost.reposts = fallbackWallPost.reposts ?? new Reposts();
            this.CompleteWallCommentsRequest(
              new GetWallPostResponseData()
              {
                comments = new VKList<Comment>() { count = 0, items = new List<Comment>() },
                Users = wallUsers ?? new List<User>(),
                Users2 = new List<User>(wallUsers ?? new List<User>()),
                Groups = wallGroups ?? new List<Group>(),
                WallPost = fallbackWallPost,
                LikesAll = new LikesInfo() { count = fallbackWallPost.likes.count }
              },
              callback);
            return;
          }

          CommentsResponse commentsResponse = commentsResult.ResultData ?? new CommentsResponse();
          List<Comment> comments = commentsResponse.items ?? new List<Comment>();
          comments.Reverse();
          if (countToRead == 0)
            comments.Clear();

          List<User> users = commentsResponse.profiles ?? new List<User>();
          foreach (User wallUser in wallUsers ?? new List<User>())
          {
            if (wallUser != null && !users.Any(user => user.id == wallUser.id))
              users.Add(wallUser);
          }
          List<Group> groups = commentsResponse.groups ?? new List<Group>();
          foreach (Group wallGroup in wallGroups ?? new List<Group>())
          {
            if (wallGroup != null && !groups.Any(group => group.id == wallGroup.id))
              groups.Add(wallGroup);
          }

          GetWallPostResponseData resultData = new GetWallPostResponseData()
          {
            comments = new VKList<Comment>()
            {
              count = commentsResponse.count,
              items = comments
            },
            Users = users,
            Users2 = new List<User>(users),
            Groups = groups,
            WallPost = wallPost ?? new WallPost(),
            LikesAll = new LikesInfo()
            {
              count = wallPost != null && wallPost.likes != null
                ? wallPost.likes.count
                : 0
            },
            Poll = commentsResponse.poll
          };

          resultData.WallPost.comments = resultData.WallPost.comments ?? new Comments();
          resultData.WallPost.likes = resultData.WallPost.likes ?? new Likes();
          resultData.WallPost.reposts = resultData.WallPost.reposts ?? new Reposts();

          Action loadMissingGroups = () =>
          {
            List<long> missingGroupIds = comments
              .Where(comment => comment != null && comment.from_id < 0L)
              .Select(comment => -comment.from_id)
              .Where(groupId => !resultData.Groups.Any(group => group != null && group.id == groupId))
              .Distinct()
              .ToList();
            if (missingGroupIds.Count == 0)
            {
              this.CompleteWallCommentsRequest(resultData, callback);
              return;
            }

            VKRequestsDispatcher.DispatchRequestToVK<List<Group>>(
              "groups.getById",
              new Dictionary<string, string>()
              {
                { "group_ids", missingGroupIds.GetCommaSeparated() },
                { "fields", "photo_50,photo_100,photo_200,photo_max" }
              },
              (Action<BackendResult<List<Group>, ResultCode>>) (groupsResult =>
              {
                if (groupsResult.ResultCode == ResultCode.Succeeded &&
                    groupsResult.ResultData != null)
                  resultData.Groups.AddRange(groupsResult.ResultData);
                this.CompleteWallCommentsRequest(resultData, callback);
              }),
              (Func<string, List<Group>>) (jsonStr =>
                JsonConvert.DeserializeObject<GenericRoot<List<Group>>>(jsonStr).response ?? new List<Group>()),
              false,
              true,
              new CancellationToken?(),
              null);
          };

          List<long> missingUserIds = comments
            .Where(comment => comment != null && comment.from_id > 0L)
            .Select(comment => comment.from_id)
            .Where(userId => !resultData.Users.Any(user => user != null && user.id == userId))
            .Distinct()
            .ToList();
          if (missingUserIds.Count == 0)
          {
            loadMissingGroups();
            return;
          }

          VKRequestsDispatcher.DispatchRequestToVK<List<User>>(
            "users.get",
            new Dictionary<string, string>()
            {
              { "user_ids", missingUserIds.GetCommaSeparated() },
              { "fields", "sex,screen_name,photo_50,photo_100,photo_200,photo_max,online,online_mobile,first_name_dat,last_name_dat" }
            },
            (Action<BackendResult<List<User>, ResultCode>>) (usersResult =>
            {
              if (usersResult.ResultCode == ResultCode.Succeeded &&
                  usersResult.ResultData != null)
              {
                resultData.Users.AddRange(usersResult.ResultData);
                resultData.Users2.AddRange(usersResult.ResultData);
              }
              loadMissingGroups();
            }),
            (Func<string, List<User>>) (jsonStr =>
              JsonConvert.DeserializeObject<GenericRoot<List<User>>>(jsonStr).response ?? new List<User>()),
            false,
            true,
            new CancellationToken?(),
            null);
        }),
        (Func<string, CommentsResponse>) (jsonStr =>
          JsonConvert.DeserializeObject<GenericRoot<CommentsResponse>>(jsonStr).response ?? new CommentsResponse()),
        false,
        true,
        new CancellationToken?(),
        null);
    }

    private void CompleteWallCommentsRequest(GetWallPostResponseData resultData, Action<BackendResult<GetWallPostResponseData, ResultCode>> callback)
    {
      resultData.Users = resultData.Users ?? new List<User>();
      resultData.Users2 = resultData.Users2 ?? new List<User>();
      resultData.Groups = resultData.Groups ?? new List<Group>();
      resultData.comments = resultData.comments ?? new VKList<Comment>()
      {
        count = 0,
        items = new List<Comment>()
      };
      resultData.comments.items = resultData.comments.items ?? new List<Comment>();
      resultData.WallPost = resultData.WallPost ?? new WallPost();
      resultData.WallPost.comments = resultData.WallPost.comments ?? new Comments();
      resultData.WallPost.likes = resultData.WallPost.likes ?? new Likes();
      resultData.WallPost.reposts = resultData.WallPost.reposts ?? new Reposts();
      resultData.LikesAll = resultData.LikesAll ?? new LikesInfo();
      if (resultData.LikesAll.count > 0 &&
          resultData.WallPost.id != 0L)
      {
        VKRequestsDispatcher.DispatchRequestToVK<VKList<User>>(
          "likes.getList",
          new Dictionary<string, string>()
          {
            { "type", "post" },
            { "owner_id", resultData.WallPost.to_id.ToString() },
            { "item_id", resultData.WallPost.id.ToString() },
            { "extended", "1" },
            { "offset", "0" },
            { "count", "10" }
          },
          (Action<BackendResult<VKList<User>, ResultCode>>) (likesResult =>
          {
            if (likesResult.ResultCode == ResultCode.Succeeded &&
                likesResult.ResultData != null &&
                likesResult.ResultData.items != null)
            {
              foreach (User user in likesResult.ResultData.items)
              {
                if (user != null &&
                    !resultData.Users.Any(existing => existing != null &&
                      existing.uid == user.uid))
                  resultData.Users.Add(user);
              }
              resultData.LikesAll.users = likesResult.ResultData.items
                .Where(user => user != null)
                .Select(user => new UserLike() { uid = user.uid })
                .ToList();
            }
            this.FinishWallCommentsRequest(resultData, callback);
          }),
          (Func<string, VKList<User>>) (jsonStr =>
          {
            GenericRoot<VKList<User>> root =
              JsonConvert.DeserializeObject<GenericRoot<VKList<User>>>(jsonStr);
            return root != null && root.response != null
              ? root.response
              : new VKList<User>() { items = new List<User>() };
          }),
          false,
          true,
          new CancellationToken?(),
          null);
        return;
      }
      this.FinishWallCommentsRequest(resultData, callback);
    }

    private void FinishWallCommentsRequest(GetWallPostResponseData resultData, Action<BackendResult<GetWallPostResponseData, ResultCode>> callback)
    {
      if (resultData.Groups != null)
        GroupsService.Current.AddCachedGroups((IEnumerable<Group>) resultData.Groups);
      callback(new BackendResult<GetWallPostResponseData, ResultCode>(
        ResultCode.Succeeded,
        resultData));
    }

    public void AddComment(long postId, long ownerId, string text, long replyToCid, bool fromGroup, List<string> attachmentIds, Action<BackendResult<Comment, ResultCode>> callback, int stickerId = 0, string stickerReferrer = "")
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>()
      {
        { "owner_id", ownerId.ToString() },
        { "post_id", postId.ToString() },
        { "message", text ?? "" },
        { "from_group", fromGroup ? "1" : "0" }
      };
      if (attachmentIds != null && attachmentIds.Count > 0)
        parameters["attachments"] = attachmentIds.GetCommaSeparated(",");

      VKRequestsDispatcher.DispatchRequestToVK<WallService.CommentResponse>(
        "wall.createComment",
        parameters,
        (Action<BackendResult<WallService.CommentResponse, ResultCode>>) (createResult =>
        {
          if (createResult.ResultCode != ResultCode.Succeeded ||
              createResult.ResultData == null)
          {
            callback(new BackendResult<Comment, ResultCode>(createResult.ResultCode));
            return;
          }

          VKRequestsDispatcher.DispatchRequestToVK<CommentsResponse>(
            "wall.getComment",
            new Dictionary<string, string>()
            {
              { "owner_id", ownerId.ToString() },
              { "comment_id", createResult.ResultData.comment_id.ToString() }
            },
            (Action<BackendResult<CommentsResponse, ResultCode>>) (commentResult =>
            {
              if (commentResult.ResultCode != ResultCode.Succeeded)
              {
                callback(new BackendResult<Comment, ResultCode>(commentResult.ResultCode));
                return;
              }

              Comment comment = commentResult.ResultData != null &&
                                commentResult.ResultData.items != null
                ? commentResult.ResultData.items.FirstOrDefault()
                : null;
              if (comment == null)
              {
                callback(new BackendResult<Comment, ResultCode>(ResultCode.DeserializationError));
                return;
              }

              comment.reply_to_comment = replyToCid;
              comment.owner_id = ownerId;
              comment.post_id = postId;
              callback(new BackendResult<Comment, ResultCode>(ResultCode.Succeeded, comment));
            }),
            (Func<string, CommentsResponse>) (jsonStr =>
              JsonConvert.DeserializeObject<GenericRoot<CommentsResponse>>(jsonStr).response ?? new CommentsResponse()),
            false,
            true,
            new CancellationToken?(),
            null);
        }),
        (Func<string, WallService.CommentResponse>) (jsonStr =>
          JsonConvert.DeserializeObject<GenericRoot<WallService.CommentResponse>>(jsonStr).response),
        false,
        true,
        new CancellationToken?(),
        null);
    }

    public void DeleteComment(long ownerId, long cid, Action<BackendResult<ResponseWithId, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["owner_id"] = ownerId.ToString();
      parameters["comment_id"] = cid.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<ResponseWithId>("wall.deleteComment", parameters, callback, (Func<string, ResponseWithId>) (jsonStr => new ResponseWithId()), false, true, new CancellationToken?(),  null);
    }

    public void GetWallById(long ownerId, long postId, Action<BackendResult<WallData, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["posts"] = ownerId.ToString() + "_" + postId.ToString();
      parameters["extended"] = "1";
      VKRequestsDispatcher.DispatchRequestToVK<WallData>("wall.getById", parameters, callback, (Func<string, WallData>) (jsonStr =>
      {
        jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "items", false);
        jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "profiles", false);
        jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "groups", false);
        return JsonConvert.DeserializeObject<GenericRoot<WallData>>(jsonStr).response;
      }), false, true, new CancellationToken?(),  null);
    }

    public Task<BackendResult<WallData, ResultCode>> GetWall(long ownerId, int offset, int count, string filter = "all")
    {
      TaskCompletionSource<BackendResult<WallData, ResultCode>> tcs = new TaskCompletionSource<BackendResult<WallData, ResultCode>>();
      this.GetWall(ownerId, offset, count, (Action<BackendResult<WallData, ResultCode>>) (res => tcs.TrySetResult(res)), filter);
      return tcs.Task;
    }

    public Task<BackendResult<List<WallData>, ResultCode>> GetWallForManyUsers(List<WallService.WallRequestData> requestData)
    {
      TaskCompletionSource<BackendResult<List<WallData>, ResultCode>> tcs = new TaskCompletionSource<BackendResult<List<WallData>, ResultCode>>();
      this.GetWallForManyUsers(requestData, (Action<BackendResult<List<WallData>, ResultCode>>) (res => tcs.TrySetResult(res)));
      return tcs.Task;
    }

    public void GetWallForManyUsers(List<WallService.WallRequestData> requestData, Action<BackendResult<List<WallData>, ResultCode>> callback)
    {
      StringBuilder stringBuilder1 = new StringBuilder().AppendFormat("var users =  API.users.get({{\"user_ids\":\"{0}\", \"fields\":\"wall_default\"}});", new object[1]
      {
        requestData.Select<WallService.WallRequestData, long>((Func<WallService.WallRequestData, long>) (r => r.UserId)).ToList<long>().GetCommaSeparated()
      }).Append(Environment.NewLine);
      for (int index = 0; index < requestData.Count; ++index)
      {
        WallService.WallRequestData wallRequestData = requestData[index];
        stringBuilder1 = stringBuilder1.AppendFormat("var wall{3} = API.wall.get({{\"owner_id\":{0}, \"offset\":{1}, \"count\":{2}, \"extended\":1, \"filter\":users[{3}].wall_default}});", wallRequestData.UserId, wallRequestData.Offset, wallRequestData.Count, index).AppendFormat(Environment.NewLine);
      }
      StringBuilder stringBuilder2 = stringBuilder1.Append("return {");
      for (int index = 0; index < requestData.Count; ++index)
      {
        if (index > 0)
          stringBuilder2 = stringBuilder2.Append(", ");
        stringBuilder2 = stringBuilder2.AppendFormat("\"Wall{0}\":wall{0} ", new object[1]
        {
          index
        });
      }
      VKRequestsDispatcher.Execute<List<WallData>>(stringBuilder2.Append("};").ToString(), callback, (Func<string, List<WallData>>) (jsonStr =>
      {
        List<WallData> wallDataList1 = new List<WallData>();
        jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "wall", false);
        jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "profiles", false);
        jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "groups", false);
        for (int index = 0; index < requestData.Count; ++index)
          jsonStr = VKRequestsDispatcher.FixFalseArray(jsonStr, "Wall" + index, true);
        List<int> resultCounts;
        jsonStr = VKRequestsDispatcher.GetArrayCountsAndRemove(jsonStr, "wall", out resultCounts);
        GenericRoot<Dictionary<string, WallData>> genericRoot = JsonConvert.DeserializeObject<GenericRoot<Dictionary<string, WallData>>>(jsonStr);
        List<WallData> wallDataList2 = new List<WallData>();
        foreach (KeyValuePair<string, WallData> keyValuePair in genericRoot.response)
          wallDataList2.Add(keyValuePair.Value);
        return wallDataList2;
      }), false, true, new CancellationToken?());
    }

    public void GetWall(long ownerId, int offset, int count, Action<BackendResult<WallData, ResultCode>> callback, string filter = "all")
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary["owner_id"] = ownerId.ToString();
      dictionary["offset"] = offset.ToString();
      dictionary["count"] = count.ToString();
      dictionary["extended"] = "1";
      dictionary["filter"] = filter;
      string methodName = "wall.get";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<WallData, ResultCode>> callback1 = callback;
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type
      
      VKRequestsDispatcher.DispatchRequestToVK<WallData>(methodName, parameters, callback1, (Func<string, WallData>) (jsonStr => JsonConvert.DeserializeObject<GenericRoot<WallData>>(jsonStr).response), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void Repost(long ownerId, long obj_id, string message, RepostObject obj, long gid, Action<BackendResult<RepostResult, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      string str = obj.ToString() + ownerId.ToString() + "_" + obj_id.ToString();
      parameters["object"] = str;
      if (!string.IsNullOrEmpty(message))
        parameters["message"] = message;
      if (gid != 0L)
        parameters["group_id"] = gid.ToString();
      VKRequestsDispatcher.DispatchRequestToVK<RepostResult>("wall.repost", parameters, (Action<BackendResult<RepostResult, ResultCode>>) (res =>
      {
        if (res.ResultCode == ResultCode.Succeeded)
          EventAggregator.Current.Publish(new RepostedObjectEvent()
          {
            owner_id = ownerId,
            obj_id = obj_id,
            rObj = obj,
            groupId = gid,
            RepostResult = res.ResultData
          });
        callback(res);
      }),  null, false, true, new CancellationToken?(),  null);
    }

    public void DeletePost(long ownerId, long post_id)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      dictionary["owner_id"] = ownerId.ToString();
      dictionary["post_id"] = post_id.ToString();
      string methodName = "wall.delete";
      Dictionary<string, string> parameters = dictionary;
      Action<BackendResult<object, ResultCode>> action = (Action<BackendResult<object, ResultCode>>) (res => {});
      int num1 = 0;
      int num2 = 1;
      CancellationToken? cancellationToken = new CancellationToken?();
      // ISSUE: variable of the null type

      Action<BackendResult<object, ResultCode>> callback = (Action<BackendResult<object, ResultCode>>)(res => { });
      VKRequestsDispatcher.DispatchRequestToVK<object>(methodName, parameters, callback, (Func<string, object>) (jsonstr => new object()), num1 != 0, num2 != 0, cancellationToken, null);
    }

    public void EditComment(WallPostRequestData data, Action<BackendResult<long, ResultCode>> callback)
    {
      Dictionary<string, string> parameters = new Dictionary<string, string>();
      parameters["owner_id"] = data.owner_id.ToString();
      parameters["comment_id"] = data.comment_id.ToString();
      parameters["message"] = data.message;
      if (!data.AttachmentIds.IsNullOrEmpty())
        parameters["attachments"] = data.AttachmentIds.GetCommaSeparated(",");
      VKRequestsDispatcher.DispatchRequestToVK<long>("wall.editComment", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void Search(long ownerId, string domain, string query, int count, int offset, Action<BackendResult<WallData, ResultCode>> callback)
    {
      Dictionary<string, string> dictionary = new Dictionary<string, string>();
      string index1 = "query";
      string str1 = query;
      dictionary[index1] = str1;
      string index2 = "count";
      string str2 = count.ToString();
      dictionary[index2] = str2;
      string index3 = "offset";
      string str3 = offset.ToString();
      dictionary[index3] = str3;
      string index4 = "extended";
      string str4 = "1";
      dictionary[index4] = str4;
      Dictionary<string, string> parameters = dictionary;
      if (ownerId != 0L)
        parameters["owner_id"] = ownerId.ToString();
      else if (!string.IsNullOrEmpty(domain))
        parameters["domain"] = domain;
      VKRequestsDispatcher.DispatchRequestToVK<WallData>("wall.search", parameters, callback,  null, false, true, new CancellationToken?(),  null);
    }

    public void SendStats(List<WallPost> viewedWallPosts)
    {
      // stats.viewPosts is not implemented by OpenVK.
    }

    public class ResponseWithPostId
    {
      public WallService.ResponseWithPostId.Post response { get; set; }

      public class Post
      {
        public long post_id { get; set; }
      }
    }

    public class CommentResponse
    {
      public long comment_id { get; set; }
    }

    public class WallRequestData
    {
      public long UserId { get; set; }

      public int Offset { get; set; }

      public int Count { get; set; }
    }
  }
}
