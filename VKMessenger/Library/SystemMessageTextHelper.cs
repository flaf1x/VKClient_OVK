using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Localization;
using VKMessenger.Backend;

namespace VKMessenger.Library
{
  public class SystemMessageTextHelper
  {
    public static string GenerateText(Message message, User user1, User user2, bool extendedText)
    {
      if (message == null)
        return "";
      if (user1 == null)
        user1 = new User();
      if (user2 == null)
        user2 = new User();
      string str1 = SystemMessageTextHelper.CreateUserNameText(user1, false, extendedText);
      string str2 = user2.id > -2000000000L ? SystemMessageTextHelper.CreateUserNameText(user2, true, extendedText) : message.action_email;
      if (!extendedText)
        str1 = "";
      string str3 = "";
      string action = message.action;
      if (action == "chat_invite_user_by_link")
        return (str1 + (user1.IsFemale ? " присоединилась к беседе по ссылке" : " присоединился к беседе по ссылке")).Trim();
      if (action == "chat_pin_message") return (str1 + (user1.IsFemale ? " закрепила сообщение" : " закрепил сообщение")).Trim();
      if (action == "chat_unpin_message") return (str1 + (user1.IsFemale ? " открепила сообщение" : " открепил сообщение")).Trim();
      if (action == "chat_moderator_add") return (str1 + (user1.IsFemale ? " назначила администратором " : " назначил администратором ") + str2).Trim();
      if (action == "chat_moderator_remove") return (str1 + (user1.IsFemale ? " сняла права администратора у " : " снял права администратора у ") + str2).Trim();
      if (!(action == "chat_photo_update"))
      {
        if (!(action == "chat_photo_remove"))
        {
          if (!(action == "chat_create"))
          {
            if (!(action == "chat_title_update"))
            {
              if (!(action == "chat_invite_user"))
              {
                if (action == "chat_kick_user")
                  str3 = message.action_mid != (long) message.uid ? (!user1.IsFemale ? string.Format(CommonResources.ChatKickoutMaleFrm, str1, str2) : string.Format(CommonResources.ChatKickoutFemaleFrm, str1, str2)) : (!user1.IsFemale ? string.Format(CommonResources.ChatLeftConversationMaleFrm, str1) : string.Format(CommonResources.ChatLeftConversationFemaleFrm, str1));
              }
              else
                str3 = message.action_mid != (long) message.uid ? (!user1.IsFemale ? string.Format(CommonResources.ChatInviteMaleFrm, str1, str2) : string.Format(CommonResources.ChatInviteFemaleFrm, str1, str2)) : (!user1.IsFemale ? string.Format(CommonResources.ChatReturnedToConversationMaleFrm, str1) : string.Format(CommonResources.ChatReturnedToConversationFemaleFrm, str1));
            }
            else
              str3 = !user1.IsFemale ? string.Format(CommonResources.ChatTitleUpdateMaleFrm, str1, message.action_text) : string.Format(CommonResources.ChatTitleUpdateFemaleFrm, str1, message.action_text);
          }
          else
            str3 = !user1.IsFemale ? string.Format(CommonResources.ChatCreateMaleFrm, str1, message.action_text) : string.Format(CommonResources.ChatCreateFemaleFrm, str1, message.action_text);
        }
        else
          str3 = !user1.IsFemale ? string.Format(CommonResources.ChatPhotoDeleteMaleFrm, str1) : string.Format(CommonResources.ChatPhotoDeleteFemaleFrm, str1);
      }
      else
        str3 = !user1.IsFemale ? string.Format(CommonResources.ChatPhotoUpdateMaleFrm, str1) : string.Format(CommonResources.ChatPhotoUpdateFemaleFrm, str1);
      // New server actions must not become empty, zero-height service rows.
      return string.IsNullOrWhiteSpace(str3) ? (!string.IsNullOrWhiteSpace(message.body) ? message.body : "Событие беседы") : str3.Trim();
    }

    private static string CreateUserNameText(User user, bool isAcc, bool extendedText)
    {
      string str = isAcc ? user.NameAcc : user.Name;
      if (string.IsNullOrWhiteSpace(str)) str = user.Name;
      if (string.IsNullOrWhiteSpace(str)) return "Участник";
      if (!extendedText)
        return str;
      if (user.id > 0L)
        return string.Format("[id{0}|{1}]", user.id, str);
      return string.Format("[club{0}|{1}]", -user.id, str);
    }
  }
}
