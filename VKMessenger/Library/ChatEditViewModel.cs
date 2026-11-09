using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Windows;
using System.Windows.Navigation;
using VKClient.Audio.Base;
using VKClient.Audio.Base.Events;
using VKClient.Audio.Base.Utils;
using VKClient.Common;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Localization;
using VKClient.Common.UC;
using VKClient.Common.Utils;
using VKMessenger.Backend;

namespace VKMessenger.Library
{
  public class ChatEditViewModel : ViewModelStatefulBase, IHandle<NotificationSettingsChangedEvent>
  {
    private bool _isTitleBoxEnabled = true;
    private long _chatId;
    private ChatInfo _chatInformation;
    private NavigationService _navigationService;
    private bool _isPhotoChanging;

    public bool CanManageChat
    {
      get { return this._chatInformation != null && this._chatInformation.chat.CanManage(
        AppGlobalStateManager.Current.LoggedInUserId, "can_change_info"); }
    }

    public bool CanManageMembers
    {
      get { return this._chatInformation != null && this._chatInformation.chat.CanManage(
        AppGlobalStateManager.Current.LoggedInUserId, "can_invite"); }
    }
    public bool IsTitleReadOnly { get { return !this.CanManageChat; } }

    private void PublishMetadata()
    {
      EventAggregator.Current.Publish(new NotificationSettingsChangedEvent {
        ChatId = this.ChatId, IsChatMetadataChange = true, Title = this.Title,
        Photo = this._chatInformation.chat.photo_200 });
    }

    public ObservableCollection<ChatMember> Members { get; private set; }

    public long ChatId
    {
      get
      {
        return this._chatId;
      }
    }

    public long PeerId
    {
      get
      {
        return 2000000000L + this._chatId;
      }
    }

    public string Title
    {
      get
      {
        ChatInfo chatInformation = this._chatInformation;
        return (chatInformation != null ? Extensions.ForUI(chatInformation.chat.title) :  null) ?? "";
      }
    }

    public string Photo
    {
      get
      {
        ChatInfo chatInformation = this._chatInformation;
        return (chatInformation != null ? AvatarHelper.Prepare(chatInformation.chat.photo_200) :  null) ?? "";
      }
    }

    public Visibility PhotoPlaceholderVisibility
    {
      get
      {
        if (!string.IsNullOrEmpty(this.Photo))
          return Visibility.Collapsed;
        return Visibility.Visible;
      }
    }

    public bool IsPhotoMenuEnabled
    {
      get
      {
          if (this.CanManageChat && this.PhotoPlaceholderVisibility == Visibility.Collapsed)
          return !this.IsPhotoChanging;
        return false;
      }
    }

    public bool IsNotificationsSoundEnabled
    {
      get
      {
        ChatInfo chatInformation = this._chatInformation;
        if (chatInformation == null)
          return true;
        return !chatInformation.chat.push_settings.AreDisabledNow;
      }
    }

    public string NotificationsSoundMode
    {
      get
      {
        if (!this.IsNotificationsSoundEnabled)
          return CommonResources.NotificationsSound_Disabled;
        return CommonResources.NotificationsSound_Enabled;
      }
    }

    public bool IsTitleBoxEnabled
    {
      get
      {
        return this.CanManageChat && this._isTitleBoxEnabled;
      }
      set
      {
        this._isTitleBoxEnabled = value;
        this.NotifyPropertyChanged<bool>((Expression<Func<bool>>) (() => this.IsTitleBoxEnabled));
      }
    }

    public bool IsPhotoChanging
    {
      get
      {
        return this._isPhotoChanging;
      }
      set
      {
        this._isPhotoChanging = value;
        this.NotifyPropertyChanged<bool>((Expression<Func<bool>>) (() => this.IsPhotoChanging));
        this.NotifyPropertyChanged<bool>((Expression<Func<bool>>) (() => this.IsPhotoMenuEnabled));
      }
    }

    public bool IsNotificationsSoundModeSwitching { get; private set; }

    public bool IsMemberAdding { get; private set; }

    public bool IsChatLeaving { get; private set; }

    public ChatEditViewModel(long chatId, NavigationService navigationService)
    {
      this.Members = new ObservableCollection<ChatMember>();
      this._chatId = chatId;
      this._navigationService = navigationService;
      EventAggregator.Current.Subscribe(this);
    }

    public void Handle(NotificationSettingsChangedEvent message)
    {
      if (message.ChatId != this.ChatId || message.IsChatMetadataChange || this._chatInformation == null) return;
      this._chatInformation.chat.push_settings = message.PushSettings ?? new VKMessenger.Backend.PushSettings {
        disabled_until = message.AreNotificationsDisabled ? -1 : 0, sound = message.AreNotificationsDisabled ? 0 : 1 };
      this.NotifyPropertyChanged<bool>(() => this.IsNotificationsSoundEnabled);
      this.NotifyPropertyChanged<string>(() => this.NotificationsSoundMode);
    }

    public override void Load(Action<ResultCode> callback)
    {
      BackendServices.ChatService.GetChatInfo(this._chatId, (Action<BackendResult<ChatInfo, ResultCode>>) (result =>
      {
        if (result.ResultCode == ResultCode.Succeeded)
        {
          this._chatInformation = result.ResultData;
          Execute.ExecuteOnUIThread((Action) (() =>
          {
            long chatCreatorId;
            long.TryParse(this._chatInformation.chat.admin_id, out chatCreatorId);
            this.Members.Clear();
            foreach (ChatUser chatParticipant in this._chatInformation.chat_participants)
            {
              ChatUser member = chatParticipant;
              User invitedByUser = this._chatInformation.invited_by_users.FirstOrDefault<User>((Func<User, bool>) (u => u.id == member.invited_by));
              var item = new ChatMember(member, invitedByUser, chatCreatorId);
              if (!this._chatInformation.chat.IsAdministrator(AppGlobalStateManager.Current.LoggedInUserId))
                item.ExcludeButtonVisibility = Visibility.Collapsed;
              this.Members.Add(item);
            }
            this.NotifyPropertyChanged<string>((Expression<Func<string>>) (() => this.Title));
            this.NotifyPropertyChanged<string>((Expression<Func<string>>) (() => this.Photo));
            this.NotifyPropertyChanged<Visibility>((Expression<Func<Visibility>>) (() => this.PhotoPlaceholderVisibility));
            this.NotifyPropertyChanged<bool>((Expression<Func<bool>>) (() => this.IsPhotoMenuEnabled));
            this.NotifyPropertyChanged<bool>(() => this.CanManageChat);
            this.NotifyPropertyChanged<bool>(() => this.IsTitleReadOnly);
            this.NotifyPropertyChanged<bool>(() => this.CanManageMembers);
            this.NotifyPropertyChanged<bool>(() => this.IsTitleBoxEnabled);
            this.NotifyPropertyChanged<string>((Expression<Func<string>>) (() => this.NotificationsSoundMode));
            this.PublishMetadata();
            this.IsNotificationsSoundModeSwitching = true;
            AccountService.Instance.GetPeerPushSettings(this.PeerId, settings => Execute.ExecuteOnUIThread(() => {
              this.IsNotificationsSoundModeSwitching = false;
              if (settings.ResultCode != ResultCode.Succeeded || settings.ResultData == null) return;
              this._chatInformation.chat.push_settings = settings.ResultData;
              this.NotifyPropertyChanged<bool>(() => this.IsNotificationsSoundEnabled);
              this.NotifyPropertyChanged<string>(() => this.NotificationsSoundMode);
              EventAggregator.Current.Publish(new NotificationSettingsChangedEvent {
                ChatId = this.ChatId, AreNotificationsDisabled = settings.ResultData.AreDisabledNow, PushSettings = settings.ResultData });
            }));
          }));
        }
        callback(result.ResultCode);
      }));
    }

    public void ChangeTitle(string newTitle, Action errorAction)
    {
      if (!this.CanManageChat) { errorAction(); return; }
      if (!this._isTitleBoxEnabled) return;
      newTitle = (newTitle ?? "").Trim();
      if (newTitle == this.Title) return;
      if (string.IsNullOrWhiteSpace(newTitle) || newTitle.Length < 2)
      {
        errorAction();
      }
      else
      {
        this.SetInProgress(true, "");
        this.IsTitleBoxEnabled = false;
        BackendServices.ChatService.EditChat(this._chatId, newTitle, (Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>>) (result => Execute.ExecuteOnUIThread((Action) (() =>
        {
          if (result.ResultCode == ResultCode.Succeeded)
          {
            this._chatInformation.chat.title = newTitle;
            this.NotifyPropertyChanged<string>((Expression<Func<string>>) (() => this.Title));
            this.PublishMetadata();
          }
          else
          {
            errorAction();
            GenericInfoUC.ShowBasedOnResult(result.ResultCode, "", (VKRequestsDispatcher.Error) null);
          }
          this.SetInProgress(false, "");
          this.IsTitleBoxEnabled = true;
        }))));
      }
    }

    public void UpdatePhoto(Stream photoStream, Rect crop)
    {
      if (!this.CanManageChat || this.IsPhotoChanging || photoStream == null) return;
      this.IsPhotoChanging = true;
      this.SetInProgress(true, "");
      ImagePreprocessor.PrepareChatAvatar(photoStream, crop, photoData => Execute.ExecuteOnUIThread(() =>
      {
        if (photoData == null)
        {
          this.SetInProgress(false, ""); this.IsPhotoChanging = false;
          GenericInfoUC.ShowBasedOnResult(ResultCode.UploadingFailed, "", null);
          return;
        }
        BackendServices.MessagesService.UpdateChatPhoto(this.ChatId, photoData, new Rect(), (Action<BackendResult<ChatInfoWithMessageId, ResultCode>>) (result => Execute.ExecuteOnUIThread((Action) (() =>
        {
          if (result.ResultCode == ResultCode.Succeeded)
          {
            this._chatInformation.chat.photo_200 = result.ResultData != null && result.ResultData.chat != null
              ? result.ResultData.chat.photo_200 : null;
            this.PublishMetadata();
            this.Reload(false);
            this.NotifyPropertyChanged<string>((Expression<Func<string>>) (() => this.Photo));
            this.NotifyPropertyChanged<Visibility>((Expression<Func<Visibility>>) (() => this.PhotoPlaceholderVisibility));
            this.NotifyPropertyChanged<bool>((Expression<Func<bool>>) (() => this.IsPhotoMenuEnabled));
          }
          else
            GenericInfoUC.ShowBasedOnResult(result.ResultCode, "", (VKRequestsDispatcher.Error) null);
          this.SetInProgress(false, "");
          this.IsPhotoChanging = false;
        }))));
      }));
    }

    public void DeletePhoto()
    {
      if (!this.CanManageChat || this.IsPhotoChanging) return;
      this.IsPhotoChanging = true;
      this.SetInProgress(true, "");
      BackendServices.MessagesService.DeleteChatPhoto(this.ChatId, (Action<BackendResult<ChatInfoWithMessageId, ResultCode>>) (result => Execute.ExecuteOnUIThread((Action) (() =>
      {
        if (result.ResultCode == ResultCode.Succeeded)
        {
          this._chatInformation.chat.photo_200 =  null;
          this.PublishMetadata();
          this.NotifyPropertyChanged<string>((Expression<Func<string>>) (() => this.Photo));
          this.NotifyPropertyChanged<Visibility>((Expression<Func<Visibility>>) (() => this.PhotoPlaceholderVisibility));
          this.NotifyPropertyChanged<bool>((Expression<Func<bool>>) (() => this.IsPhotoMenuEnabled));
        }
        else
          GenericInfoUC.ShowBasedOnResult(result.ResultCode, "", (VKRequestsDispatcher.Error) null);
        this.SetInProgress(false, "");
        this.IsPhotoChanging = false;
      }))));
    }

    public void SwitchNotificationsSoundMode()
    {
      if (this._chatInformation == null || this.IsNotificationsSoundModeSwitching) return;
      bool disable = this.IsNotificationsSoundEnabled;
      this.IsNotificationsSoundModeSwitching = true;
      this.SetInProgress(true, "");
      AccountService.Instance.SetSilenceMode(AppGlobalStateManager.Current.GlobalState.NotificationsUri, disable ? -1 : 0, (Action<BackendResult<object, ResultCode>>) (result => Execute.ExecuteOnUIThread((Action) (() =>
      {
        if (result.ResultCode == ResultCode.Succeeded)
        {
          this._chatInformation.chat.push_settings.disabled_until = disable ? -1 : 0;
          this._chatInformation.chat.push_settings.disabled_forever = disable;
          this._chatInformation.chat.push_settings.sound = disable ? 0 : 1;
          this._chatInformation.chat.push_settings.no_sound = disable;
          EventAggregator.Current.Publish(new NotificationSettingsChangedEvent {
            ChatId = this.ChatId, AreNotificationsDisabled = disable, PushSettings = this._chatInformation.chat.push_settings });
          this.NotifyPropertyChanged<bool>((Expression<Func<bool>>) (() => this.IsNotificationsSoundEnabled));
          this.NotifyPropertyChanged<string>((Expression<Func<string>>) (() => this.NotificationsSoundMode));
        }
        else
          GenericInfoUC.ShowBasedOnResult(result.ResultCode, "", (VKRequestsDispatcher.Error) null);
        this.SetInProgress(false, "");
        this.IsNotificationsSoundModeSwitching = false;
      }))), this.ChatId, 0L);
    }

    public void AddMember(User user)
    {
      if (!this.CanManageMembers || this.IsMemberAdding || user == null) return;
      this.IsMemberAdding = true;
      this.SetInProgress(true, "");
      IChatService chatService = BackendServices.ChatService;
      long chatId = this._chatId;
      List<long> userIds = new List<long>();
      userIds.Add(user.id);
      Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback = (Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>>) (result => Execute.ExecuteOnUIThread((Action) (() =>
      {
        if (result.ResultCode == ResultCode.Succeeded)
        {
          User loggedInUser = AppGlobalStateManager.Current.GlobalState.LoggedInUser;
          this.Members.Add(new ChatMember(new ChatUser(user, loggedInUser.id), loggedInUser, long.Parse(this._chatInformation.chat.admin_id)));
        }
        else
          GenericInfoUC.ShowBasedOnResult(result.ResultCode, "", (VKRequestsDispatcher.Error) null);
        this.SetInProgress(false, "");
        this.IsMemberAdding = false;
      })));
      chatService.AddChatUsers(chatId, userIds, callback);
    }

    public void ExcludeMember(ChatMember member)
    {
      if (this._chatInformation == null || !this._chatInformation.chat.IsAdministrator(AppGlobalStateManager.Current.LoggedInUserId) ||
          member == null || member.ExcludeButtonVisibility != Visibility.Visible) return;
      this.SetInProgress(true, "");
      member.ExcludeButtonVisibility = Visibility.Collapsed;
      IChatService chatService = BackendServices.ChatService;
      long chatId = this._chatId;
      List<long> usersToBeRemoved = new List<long>();
      usersToBeRemoved.Add(member.Id);
      Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback = (Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>>) (result => Execute.ExecuteOnUIThread((Action) (() =>
      {
        if (result.ResultCode == ResultCode.Succeeded)
        {
          this.Members.Remove(member);
        }
        else
        {
          member.ExcludeButtonVisibility = Visibility.Visible;
          GenericInfoUC.ShowBasedOnResult(result.ResultCode, "", (VKRequestsDispatcher.Error) null);
        }
        this.SetInProgress(false, "");
      })));
      chatService.RemoveChatUsers(chatId, usersToBeRemoved, callback);
    }

    public void LeaveChat()
    {
      this.IsChatLeaving = true;
      this.SetInProgress(true, "");
      IChatService chatService = BackendServices.ChatService;
      long chatId = this._chatId;
      List<long> usersToBeRemoved = new List<long>();
      usersToBeRemoved.Add(AppGlobalStateManager.Current.LoggedInUserId);
      Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>> callback = (Action<BackendResult<VKClient.Common.Backend.DataObjects.ResponseWithId, ResultCode>>) (result => Execute.ExecuteOnUIThread((Action) (() =>
      {
        if (result.ResultCode == ResultCode.Succeeded)
        {
          this._navigationService.RemoveBackEntrySafe();
          this._navigationService.GoBackSafe();
        }
        else
          GenericInfoUC.ShowBasedOnResult(result.ResultCode, "", (VKRequestsDispatcher.Error) null);
        this.SetInProgress(false, "");
        this.IsChatLeaving = false;
      })));
      chatService.RemoveChatUsers(chatId, usersToBeRemoved, callback);
    }
  }
}
