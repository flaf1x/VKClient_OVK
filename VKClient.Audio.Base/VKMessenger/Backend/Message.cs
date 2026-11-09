using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VKClient.Audio.Base.Utils;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Utils;

namespace VKMessenger.Backend
{
    public class Message : IBinarySerializable
    {
        //private static string _delimStr = "###$$$%%%***";
        private string _title = "";
        private string _body = "";
        private string _photo_100 = "";
        private string _action = "";
        private string _action_email = "";
        private string _action_text = "";
        private string _photo_200 = "";

        public int mid
        {
            get
            {
                return this.id;
            }
            set
            {
                this.id = value;
            }
        }

        public int id { get; set; }
        public long peer_id { get; set; }
        public int conversation_message_id { get; set; }
        public long random_id { get; set; }
        public Message reply_message { get; set; }

        public bool IsAtOrBeforeReadBoundary(long boundary)
        {
            // OpenVK long-poll 6/7 carries a peer-local CMID, not a global ID.
            return boundary > 0 && conversation_message_id > 0 && conversation_message_id <= boundary;
        }

        public void MergeReadState(Message latest)
        {
            if (latest == null) return;
            if (latest.conversation_message_id > 0) this.conversation_message_id = latest.conversation_message_id;
            this.read_state = Math.Max(this.read_state, latest.read_state);
        }

        public Message CreateReplySnapshot()
        {
            // A quote is a bounded preview, not a recursive copy of the chat.
            return new Message {
                id = this.id, conversation_message_id = this.conversation_message_id,
                peer_id = this.peer_id, from_id = this.from_id, uid = this.uid,
                chat_id = this.chat_id, date = this.date, body = this.body,
                @out = this.@out, read_state = this.read_state, deleted = this.deleted,
                attachments = this.attachments, sticker_id = this.sticker_id, geo = this.geo,
                fwd_messages = this.fwd_messages == null ? null : this.fwd_messages.Where(m => m != null).Take(1).Select(m => new Message { id = m.id, body = m.body }).ToList()
            };
        }

        public int date { get; set; }

        public int @out { get; set; }

        public int uid
        {
            get
            {
                return this.user_id;
            }
            set
            {
                this.user_id = value;
            }
        }

        public int user_id { get; set; }

        public int read_state { get; set; }

        public string title
        {
            get
            {
                return this._title;
            }
            set
            {
                this._title = (value ?? "").ForUI();
            }
        }

        public string body
        {
            get
            {
                return this._body;
            }
            set
            {
                this._body = (value ?? "").ForUI();
            }
        }

        public int sticker_id { get; set; }

        public List<Attachment> attachments { get; set; }

        public Geo geo { get; set; }

        public List<Message> fwd_messages { get; set; }

        public int chat_id { get; set; }

        public string chat_active_str
        {
            get
            {
                if (this.chat_active != null)
                    return this.chat_active.GetCommaSeparated();
                return "";
            }
            set
            {
                if (value == null)
                    this.chat_active = null;
                else
                    this.chat_active = value.ParseCommaSeparated();
            }
        }

        public List<long> chat_active { get; set; }

        public int users_count { get; set; }

        public int admin_id { get; set; }

        public PushSettings push_settings { get; set; }

        // Runtime provenance: a history/long-poll message has no chat settings.
        public bool HasChatMetadata { get; set; }
        public bool HasPushSettings { get; set; }

        public void PreserveConversationSettings(Message previous)
        {
            if (previous == null || chat_id == 0 || chat_id != previous.chat_id) return;
            if (!HasChatMetadata)
            {
                if (string.IsNullOrEmpty(photo_200)) photo_200 = previous.photo_200;
                if (string.IsNullOrEmpty(title)) title = previous.title;
                if (string.IsNullOrEmpty(chat_active_str)) chat_active_str = previous.chat_active_str;
                if (action == "chat_photo_remove") photo_200 = "";
            }
            if (!HasPushSettings) push_settings = previous.push_settings;
        }

        public string photo_100
        {
            get
            {
                return this._photo_100;
            }
            set
            {
                this._photo_100 = value ?? "";
            }
        }

        public string action
        {
            get
            {
                return this._action;
            }
            set
            {
                this._action = value ?? "";
            }
        }

        public long action_mid { get; set; }

        public string action_email
        {
            get
            {
                return this._action_email;
            }
            set
            {
                this._action_email = (value ?? "").ForUI();
            }
        }

        public string action_text
        {
            get
            {
                return this._action_text;
            }
            set
            {
                this._action_text = (value ?? "").ForUI();
            }
        }

        public int important { get; set; }

        public int deleted { get; set; }

        public string photo_200
        {
            get
            {
                return this._photo_200;
            }
            set
            {
                this._photo_200 = value ?? "";
            }
        }

        public long from_id { get; set; }

        public Message()
        {
            this.push_settings = new PushSettings();
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(6);
            writer.Write(this.mid);
            writer.Write(this.date);
            writer.Write(this.@out);
            writer.Write(this.uid);
            writer.Write(this.read_state);
            writer.WriteString(this.title);
            writer.WriteString(this.photo_100);
            writer.WriteString(this.action);
            writer.Write(this.push_settings.disabled_until);
            writer.WriteString(this.body);
            writer.Write(this.chat_id);
            writer.WriteString(this.chat_active_str);
            writer.Write(this.admin_id);
            writer.WriteList<Message>((IList<Message>)this.fwd_messages, 10000);
            writer.WriteList<Attachment>((IList<Attachment>)this.attachments, 10000);
            writer.Write<Geo>(this.geo, false);
            writer.Write(this.important);
            writer.Write(this.deleted);
            writer.Write(this.users_count);
            writer.Write(this.action_mid);
            writer.WriteString(this.action_email);
            writer.WriteString(this.action_text);
            writer.Write(this.sticker_id);
            writer.WriteString(this.photo_200);
            writer.Write(this.from_id);
            writer.Write(this.peer_id);
            writer.Write(this.conversation_message_id);
            writer.Write(this.random_id);
            writer.Write<Message>(this.reply_message == null ? null : this.reply_message.CreateReplySnapshot(), false);
        }

        public void Read(BinaryReader reader)
        {
            int num1 = reader.ReadInt32();
            this.mid = reader.ReadInt32();
            this.date = reader.ReadInt32();
            this.@out = reader.ReadInt32();
            this.uid = reader.ReadInt32();
            this.read_state = reader.ReadInt32();
            this.title = reader.ReadString();
            this.photo_100 = reader.ReadString();
            this.action = reader.ReadString();
            this.push_settings.disabled_until = reader.ReadInt32();
            this.body = reader.ReadString();
            this.chat_id = reader.ReadInt32();
            this.chat_active_str = reader.ReadString();
            this.admin_id = reader.ReadInt32();
            this.fwd_messages = reader.ReadList<Message>();
            this.attachments = reader.ReadList<Attachment>();
            this.geo = reader.ReadGeneric<Geo>();
            this.important = reader.ReadInt32();
            this.deleted = reader.ReadInt32();
            this.users_count = reader.ReadInt32();
            this.action_mid = reader.ReadInt64();
            this.action_email = reader.ReadString();
            this._action_text = reader.ReadString();
            this.sticker_id = reader.ReadInt32();
            int num2 = 2;
            if (num1 >= num2)
                this.photo_200 = reader.ReadString();
            int num3 = 3;
            if (num1 < num3)
                return;
            this.from_id = reader.ReadInt64();
            if (num1 >= 4)
            {
                this.peer_id = reader.ReadInt64();
                this.conversation_message_id = reader.ReadInt32();
                this.random_id = num1 >= 5 ? reader.ReadInt64() : reader.ReadInt32();
            }
            if (num1 >= 6)
                this.reply_message = reader.ReadGeneric<Message>();
        }

        public static List<long> GetAssociatedUserIds(List<Message> messages, bool includeForwarded = true)
        {
            List<long> source = new List<long>();
            foreach (Message message in messages)
                source.AddRange((IEnumerable<long>)message.GetAssociatedUserIds(includeForwarded));
            return source.Distinct<long>().ToList<long>();
        }

        public List<long> GetAssociatedUserIds(bool includeForwarded = true)
        {
            List<long> source = new List<long>();
            source.Add((long)this.uid);
            if (this.from_id != 0)
                source.Add(this.from_id);
            if (this.action_mid > 0L)
                source.Add((long)(int)this.action_mid);
            if (!string.IsNullOrWhiteSpace(this.chat_active_str))
                source.AddRange(this.chat_active_str.ParseCommaSeparated().Where<long>((Func<long, bool>)(ca => ca >= -2000000000L)));
            if (this.fwd_messages != null & includeForwarded)
            {
                foreach (Message fwdMessage in this.fwd_messages)
                    source.AddRange((IEnumerable<long>)fwdMessage.GetAssociatedUserIds(true));
            }
            if (this.reply_message != null && includeForwarded)
                source.AddRange(this.reply_message.GetAssociatedUserIds(false));
            return source.Distinct<long>().ToList<long>();
        }
    }
}
