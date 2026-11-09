using System;
using System.Linq;
using VKClient.Common.Backend;
using VKMessenger.Backend;

namespace VKMessenger.Library
{
    public static class ReplyPresentation
    {
        public static string Author(Message message)
        {
            if (message == null) return "";
            long id = message.from_id != 0 ? message.from_id : message.uid;
            var user = UsersService.Instance.GetCachedUser(id);
            return user == null ? "Сообщение" : (user.first_name + " " + user.last_name).Trim();
        }
        public static string Summary(Message message)
        {
            if (message == null) return "";
            if (message.deleted != 0) return "Сообщение удалено";
            if (!string.IsNullOrWhiteSpace(message.body))
            {
                string text = message.body.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
                return text.Length > 140 ? text.Substring(0, 140) + "…" : text;
            }
            var attachment = message.attachments == null ? null : message.attachments.FirstOrDefault(a => a != null);
            if (attachment != null)
            {
                switch (attachment.type)
                {
                    case "photo": return "Фотография";
                    case "video": return "Видео";
                    case "sticker": return "Стикер";
                    case "audio": return "Аудиозапись";
                    case "doc": return attachment.doc != null && attachment.doc.IsVoiceMessage ? "Голосовое сообщение" :
                        attachment.doc != null && attachment.doc.IsGraffiti ? "Граффити" : "Документ";
                    case "link": return "Ссылка";
                    case "wall": return "Запись";
                    case "gift": return "Подарок";
                    default: return "Вложение";
                }
            }
            if (message.geo != null) return "Местоположение";
            if (message.fwd_messages != null && message.fwd_messages.Count > 0) return "Пересланное сообщение";
            return "Сообщение";
        }
    }
}
