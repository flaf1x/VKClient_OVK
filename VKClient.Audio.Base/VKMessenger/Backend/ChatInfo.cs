using System.Collections.Generic;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend.DataObjects;

namespace VKMessenger.Backend
{
  public class ChatInfo
  {
    public Chat chat { get; set; }

    public List<ChatUser> chat_participants { get; set; }

    public List<User> invited_by_users { get; set; }
  }

  public class ChatMembersResponse
  {
    public List<ChatMemberRecord> items { get; set; }
    public List<User> profiles { get; set; }
    public List<Group> groups { get; set; }
  }

  public class ChatMemberRecord
  {
    public long member_id { get; set; }
    public long invited_by { get; set; }
    public bool can_kick { get; set; }
  }
}
