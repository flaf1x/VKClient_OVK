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
      // OpenVK does not implement the legacy messages.searchDialogs method.
      // The picker still needs a useful global search, so search people through
      // the supported users.search endpoint; loaded conversations are searched
      // locally by ConversationSearchViewModel.
      UsersService.Instance.SearchUsers(searchString, offset, count, callback);
    }
  }
}
