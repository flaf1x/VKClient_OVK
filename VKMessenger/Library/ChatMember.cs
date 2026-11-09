using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Windows;
using VKClient.Audio.Base.Utils;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Localization;
using VKMessenger.Backend;

namespace VKMessenger.Library
{
  public class ChatMember : ViewModelBase
  {
    private ChatUser _user;
    private User _invitedByUser;
    private long _chatCreatorId;
    private Visibility _excludeButtonVisibility;

    public long Id
    {
      get
      {
        return this._user.id;
      }
    }

    public string Name
    {
      get
      {
        return this._user.Name;
      }
    }

    public string Photo
    {
      get
      {
        return AvatarHelper.Prepare(this._user.photo_max);
      }
    }

    public string Information
    {
      get
      {
        if (this._user.id == this._chatCreatorId)
          return CommonResources.ChatCreator.ToLower();
        if (this._invitedByUser == null || string.IsNullOrWhiteSpace(this._invitedByUser.Name))
          return "";
        return string.Format((this._invitedByUser.sex != 1 ? CommonResources.InvitedToChatBy_M : CommonResources.InvitedToChatBy_F).ToLower(), this._invitedByUser.Name);
      }
    }

    public Visibility ExcludeButtonVisibility
    {
      get
      {
        return this._excludeButtonVisibility;
      }
      set
      {
        this._excludeButtonVisibility = value;
        this.NotifyPropertyChanged<Visibility>(() => this.ExcludeButtonVisibility);
      }
    }

    public ChatMember(ChatUser user, User invitedByUser, long chatCreatorId)
    {
      this._user = user;
      this._invitedByUser = invitedByUser;
      this._chatCreatorId = chatCreatorId;
      long loggedInUserId = AppGlobalStateManager.Current.LoggedInUserId;
      this.ExcludeButtonVisibility = user.can_kick && this._user.id != loggedInUserId ? Visibility.Visible : Visibility.Collapsed;
    }
  }
}
