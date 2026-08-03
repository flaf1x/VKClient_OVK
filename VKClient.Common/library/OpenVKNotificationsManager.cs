using Microsoft.Devices;
using Microsoft.Phone.Shell;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Windows;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.UC;
using VKClient.Common.Utils;

namespace VKClient.Common.Library
{
  public static class OpenVKNotificationsManager
  {
    private const string LastDateKey = "OpenVK.Notifications.LastDate";
    private const string LastFetchIdKey = "OpenVK.Notifications.LastFetchId";
    private static bool _isChecking;

    public static void CheckForUpdates(bool background, Action completed)
    {
      CheckForUpdates(background, (Action<bool>) (hasUpdates =>
      {
        if (completed != null)
          completed();
      }));
    }

    public static void CheckForUpdates(bool background, Action<bool> completed)
    {
      if (_isChecking || AppGlobalStateManager.Current.LoggedInUserId == 0L)
      {
        if (completed != null)
          completed(false);
        return;
      }

      _isChecking = true;
      string lastFetchId = ReadLastFetchId();
      bool establishingCursor = string.IsNullOrWhiteSpace(lastFetchId);
      if (establishingCursor)
        lastFetchId = CreateInitialFetchId();
      NewsFeedService.Current.FetchNotifications(lastFetchId,
        (Action<BackendResult<NotificationData, ResultCode>>)(res =>
        {
          bool hasUpdates = false;
          try
          {
            if (res.ResultCode == ResultCode.Succeeded && res.ResultData != null)
            {
              NotificationData data = res.ResultData;
              string nextFetchId = !string.IsNullOrWhiteSpace(data.new_lastId)
                ? data.new_lastId
                : data.next_last_id;
              if (!string.IsNullOrWhiteSpace(nextFetchId))
                SaveLastFetchId(nextFetchId);

              if (establishingCursor)
              {
                if (data.items != null && data.items.Count > 0)
                  SaveLastDate(data.items.Max(n => n.Date));
              }
              else
                hasUpdates = ProcessResult(data, background);
            }
          }
          catch (Exception ex)
          {
            Logger.Instance.Error("OpenVK notification polling failed", ex);
          }
          finally
          {
            _isChecking = false;
            if (completed != null)
              completed(hasUpdates);
          }
        }));
    }

    private static bool ProcessResult(NotificationData data, bool background)
    {
      if (data.items == null || data.items.Count == 0)
        return false;

      int newestDate = data.items.Max(n => n.Date);
      int previousDate = ReadLastDate();

      // The first successful request only establishes a cursor. Otherwise an
      // existing account would receive a toast for an old notification.
      if (previousDate == 0)
      {
        SaveLastDate(newestDate);
        return false;
      }
      if (newestDate <= previousDate)
        return false;

      Notification newest = data.items
        .Where(n => n.Date > previousDate)
        .OrderByDescending(n => n.Date)
        .FirstOrDefault(n => IsEnabledByUser(n.NotType));
      if (newest == null ||
          !AppGlobalStateManager.Current.GlobalState.PushNotificationsEnabled ||
          AppGlobalStateManager.Current.GlobalState.PushNotificationsBlockedUntil >= DateTime.UtcNow)
      {
        SaveLastDate(newestDate);
        return true;
      }

      // The first request during startup can finish before AppNotificationUC
      // is constructed. Keep the cursor in that case and retry on the next
      // polling tick instead of silently losing the banner.
      if (!background &&
          (!AppGlobalStateManager.Current.GlobalState.NotificationsEnabled ||
           AppNotificationUC.Instance == null))
      {
        if (!AppGlobalStateManager.Current.GlobalState.NotificationsEnabled)
          SaveLastDate(newestDate);
        return true;
      }

      string title;
      string text;
      string image;
      BuildDisplayData(newest, data, out title, out text, out image);
      if (background)
        ShowSystemToast(newest, title, text);
      else
        ShowInAppToast(newest, title, text, image);
      SaveLastDate(newestDate);
      return true;
    }

    private static bool IsEnabledByUser(NotificationType type)
    {
      PushSettings settings = AppGlobalStateManager.Current.GlobalState.PushSettings;
      switch (type)
      {
        case NotificationType.like_post:
        case NotificationType.like_comment:
        case NotificationType.like_photo:
        case NotificationType.like_video:
        case NotificationType.like_comment_photo:
        case NotificationType.like_comment_video:
        case NotificationType.like_comment_topic:
          return settings.like;
        case NotificationType.copy_post:
        case NotificationType.copy_photo:
        case NotificationType.copy_video:
          return settings.repost;
        case NotificationType.comment_post:
        case NotificationType.comment_photo:
        case NotificationType.comment_video:
          return settings.comment;
        case NotificationType.mention:
        case NotificationType.mention_comments:
        case NotificationType.mention_comment_photo:
        case NotificationType.mention_comment_video:
          return settings.mention;
        case NotificationType.reply_comment:
        case NotificationType.reply_topic:
        case NotificationType.reply_comment_photo:
        case NotificationType.reply_comment_video:
        case NotificationType.reply_comment_market:
          return settings.reply;
        case NotificationType.follow:
        case NotificationType.friend_accepted:
          return settings.friend;
        case NotificationType.wall:
        case NotificationType.wall_publish:
          return settings.wall_post;
        default:
          return true;
      }
    }

    private static void BuildDisplayData(Notification notification,
      NotificationData data, out string title, out string text, out string image)
    {
      User actor = FindActor(notification, data);
      Group group = null;
      if (notification.actor_id < 0L)
        group = data.groups.FirstOrDefault(g => g.id == -notification.actor_id);
      if (group == null)
        group = notification.ParsedParent as Group;
      title = actor != null && !string.IsNullOrWhiteSpace(actor.Name)
        ? actor.Name
        : group != null && !string.IsNullOrWhiteSpace(group.name)
          ? group.name
          : "OpenVK";
      image = actor != null
        ? actor.UiAvatarSmall
        : group != null
          ? group.UiAvatarSmall
          : MultiResolutionHelper.Instance.AppendResolutionSuffix(
              "/Resources/New/NotificationVK.png", true, "");
      text = GetNotificationText(notification);
    }

    private static User FindActor(Notification notification, NotificationData data)
    {
      if (notification.actor_id != 0L)
      {
        User resolved = data.profiles.FirstOrDefault(
          u => u.uid == notification.actor_id);
        if (resolved != null)
          return resolved;
      }

      User direct = notification.ParsedFeedback as User ??
                    notification.ParsedParent as User;
      if (direct != null)
        return direct;

      List<FeedbackUser> feedbackUsers =
        notification.ParsedFeedback as List<FeedbackUser>;
      if (feedbackUsers != null && feedbackUsers.Count > 0)
        return data.profiles.FirstOrDefault(u => u.uid == feedbackUsers[0].owner_id);

      Comment comment = notification.ParsedFeedback as Comment;
      if (comment != null)
        return data.profiles.FirstOrDefault(u => u.uid == comment.from_id);
      return null;
    }

    private static string GetNotificationText(Notification notification)
    {
      bool ru = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
      switch (notification.NotType)
      {
        case NotificationType.like_post:
          return ru ? "Отметил(а) вашу запись" : "Liked your post";
        case NotificationType.like_photo:
          return ru ? "Отметил(а) ваше фото" : "Liked your photo";
        case NotificationType.like_video:
          return ru ? "Отметил(а) ваше видео" : "Liked your video";
        case NotificationType.like_comment:
        case NotificationType.like_comment_photo:
        case NotificationType.like_comment_video:
        case NotificationType.like_comment_topic:
          return ru ? "Отметил(а) ваш комментарий" : "Liked your comment";
        case NotificationType.copy_post:
          return ru ? "Поделился(ась) вашей записью" : "Shared your post";
        case NotificationType.copy_photo:
          return ru ? "Поделился(ась) вашим фото" : "Shared your photo";
        case NotificationType.copy_video:
          return ru ? "Поделился(ась) вашим видео" : "Shared your video";
        case NotificationType.comment_post:
          return ru ? "Прокомментировал(а) вашу запись" : "Commented on your post";
        case NotificationType.comment_photo:
          return ru ? "Прокомментировал(а) ваше фото" : "Commented on your photo";
        case NotificationType.comment_video:
          return ru ? "Прокомментировал(а) ваше видео" : "Commented on your video";
        case NotificationType.mention:
          return ru ? "Упомянул(а) вас в записи" : "Mentioned you in a post";
        case NotificationType.mention_comments:
        case NotificationType.mention_comment_photo:
        case NotificationType.mention_comment_video:
          return ru ? "Упомянул(а) вас в комментарии" : "Mentioned you in a comment";
        case NotificationType.reply_comment:
        case NotificationType.reply_topic:
        case NotificationType.reply_comment_photo:
        case NotificationType.reply_comment_video:
        case NotificationType.reply_comment_market:
          return ru ? "Ответил(а) на ваш комментарий" : "Replied to your comment";
        case NotificationType.wall:
        case NotificationType.wall_publish:
          return ru ? "Опубликовал(а) запись на вашей стене" : "Posted on your wall";
        case NotificationType.friend_accepted:
          return ru ? "Принял(а) заявку в друзья" : "Accepted your friend request";
        case NotificationType.follow:
          return ru ? "Подписался(ась) на вас" : "Followed you";
        case NotificationType.sent_gift:
          return ru ? "Отправил(а) вам подарок" : "Sent you a gift";
        case NotificationType.voices_transfer:
          return ru ? "Перевёл(а) вам голоса" : "Sent you voices";
        case NotificationType.up_rating:
          return ru ? "Повысил(а) ваш рейтинг" : "Increased your rating";
        case NotificationType.make_you_admin:
          return ru ? "Вы назначены администратором" : "You were made an administrator";
        case NotificationType.new_posts_in_club:
          return ru ? "Новая запись в сообществе" : "New post in a community";
        case NotificationType.money_transfer_received:
          return ru ? "Вам отправили перевод" : "Sent you a transfer";
        case NotificationType.money_transfer_accepted:
          return ru ? "Ваш перевод принят" : "Accepted your transfer";
        case NotificationType.money_transfer_declined:
          return ru ? "Ваш перевод отклонён" : "Declined your transfer";
        default:
          string rawType = string.IsNullOrWhiteSpace(notification.type)
            ? "unknown"
            : notification.type;
          return ru
            ? "Новое уведомление: " + rawType
            : "New notification: " + rawType;
      }
    }

    private static void ShowInAppToast(Notification notification, string title, string text, string image)
    {
      Execute.ExecuteOnUIThread((Action)(() =>
      {
        if (AppNotificationUC.Instance != null)
          AppNotificationUC.Instance.ShowAndHideLater(image, title, text,
            (Action)(() => NavigateToNotification(notification)), null);
        if (AppGlobalStateManager.Current.GlobalState.VibrationsEnabled)
          VibrateController.Default.Start(TimeSpan.FromMilliseconds(100.0));
      }));
    }

    private static void ShowSystemToast(Notification notification, string title, string text)
    {
      ShellToast toast = new ShellToast();
      toast.Title = title;
      toast.Content = text;
      string target = notification.NotType == NotificationType.sent_gift
        ? string.Format("/VKClient.Common;component/Gifts/Views/GiftsPage.xaml?UserId={0}&FirstName=&FirstNameGen=", AppGlobalStateManager.Current.LoggedInUserId)
        : "/VKClient.Common;component/FeedbackPage.xaml";
      toast.NavigationUri = new Uri(target, UriKind.Relative);
      toast.Show();
    }

    private static void NavigateToNotification(Notification notification)
    {
      if (notification.NotType == NotificationType.sent_gift)
        Navigator.Current.NavigateToGifts(AppGlobalStateManager.Current.LoggedInUserId, "", "");
      else
        Navigator.Current.NavigateToFeedback();
    }

    private static int ReadLastDate()
    {
      object value;
      if (IsolatedStorageSettings.ApplicationSettings.TryGetValue(
          GetUserKey(LastDateKey), out value) && value is int)
        return (int)value;
      return 0;
    }

    private static void SaveLastDate(int value)
    {
      IsolatedStorageSettings.ApplicationSettings[GetUserKey(LastDateKey)] = value;
      IsolatedStorageSettings.ApplicationSettings.Save();
    }

    private static string ReadLastFetchId()
    {
      object value;
      if (IsolatedStorageSettings.ApplicationSettings.TryGetValue(
          GetUserKey(LastFetchIdKey), out value))
        return value as string;
      return null;
    }

    private static string CreateInitialFetchId()
    {
      // Redis stream ids use Unix time in milliseconds. Starting at the
      // current head avoids replaying the whole one-hour stream after logout
      // or a clean install; subsequent calls still fetch only new events.
      long milliseconds = (long) (DateTime.UtcNow -
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
      return milliseconds.ToString(CultureInfo.InvariantCulture) + "-0";
    }

    private static void SaveLastFetchId(string value)
    {
      IsolatedStorageSettings.ApplicationSettings[GetUserKey(LastFetchIdKey)] = value;
      IsolatedStorageSettings.ApplicationSettings.Save();
    }

    private static string GetUserKey(string key)
    {
      return key + "." + AppGlobalStateManager.Current.LoggedInUserId;
    }
  }
}
