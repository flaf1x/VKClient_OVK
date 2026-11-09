namespace VKClient.Audio.Base.Events
{
  public class NotificationSettingsChangedEvent
  {
    public long ChatId { get; set; }

    public bool AreNotificationsDisabled { get; set; }

    public bool IsChatMetadataChange { get; set; }
    public string Title { get; set; }
    public string Photo { get; set; }
    public VKMessenger.Backend.PushSettings PushSettings { get; set; }
  }
}
