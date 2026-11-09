using VKClient.Common.Backend.DataObjects;
using System.Collections.Generic;

namespace VKClient.Audio.Base.DataObjects
{
  public class ChatBase
  {
    private string _title = "";

    public string type { get; set; }

    public long chat_id
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

    public long id { get; set; }

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

    public string admin_id { get; set; }

    public long owner_id { get; set; }
    public List<long> admin_ids { get; set; }
    public Dictionary<string, bool> acl { get; set; }

    public bool IsAdministrator(long userId)
    {
      long legacyAdmin;
      return userId > 0 && (owner_id == userId ||
        long.TryParse(admin_id, out legacyAdmin) && legacyAdmin == userId ||
        admin_ids != null && admin_ids.Contains(userId));
    }

    public bool CanManage(long userId, string permission)
    {
      bool allowed;
      return IsAdministrator(userId) &&
        (acl == null || !acl.TryGetValue(permission, out allowed) || allowed);
    }

    public string photo_100 { get; set; }

    public string photo_200 { get; set; }

    public VKMessenger.Backend.PushSettings push_settings { get; set; }

    public ChatBase()
    {
      this.push_settings = new VKMessenger.Backend.PushSettings();
    }
  }
}
