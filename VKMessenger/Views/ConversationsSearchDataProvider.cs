using System;
using System.Collections.Generic;
using VKClient.Audio.Base;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Library;
using VKClient.Common.Utils;

namespace VKMessenger.Views
{
  public class ConversationsSearchDataProvider : ISearchDataProvider<User, FriendHeader>
  {
    public string LocalGroupName
    {
      get
      {
        return "";
      }
    }

    public string GlobalGroupName
    {
      get
      {
        return "";
      }
    }

    public IEnumerable<FriendHeader> LocalItems
    {
      get
      {
        return  null;
      }
    }

    public Func<VKList<User>, ListWithCount<FriendHeader>> ConverterFunc
    {
      get
      {
        return (Func<VKList<User>, ListWithCount<FriendHeader>>) (res =>
        {
          ListWithCount<FriendHeader> listWithCount = new ListWithCount<FriendHeader>() { TotalCount = res.count };
          foreach (User user in res.items)
            listWithCount.List.Add(new FriendHeader(user, false));
          return listWithCount;
        });
      }
    }

    public string GetFooterTextForCount(int count)
    {
      return "";
    }

    public void GetData(string searchString, Dictionary<string, string> parameters, int offset, int count, Action<BackendResult<VKList<User>, ResultCode>> callback)
    {
      MessagesService.Instance.SearchDialogs(searchString, res =>
      {
        if (res.ResultCode != ResultCode.Succeeded)
        {
          callback(new BackendResult<VKList<User>, ResultCode>(res.ResultCode));
          return;
        }
        List<User> users = new List<User>();
        foreach (object item in res.ResultData)
          if (item is User)
            users.Add((User)item);
        callback(new BackendResult<VKList<User>, ResultCode>(ResultCode.Succeeded,
          new VKList<User>() { count = users.Count, items = users.GetRange(Math.Min(offset, users.Count), Math.Min(count, Math.Max(0, users.Count - offset))) }));
      });
    }
  }
}
