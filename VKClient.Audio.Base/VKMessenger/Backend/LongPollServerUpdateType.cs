namespace VKMessenger.Backend
{
  public enum LongPollServerUpdateType
  {
    IncomingMessagesRead = 6,
    OutgoingMessagesRead = 7,
    MessageHasBeenEdited = 5,
    SynchronizationRequired = 1000001,
    UserBecameOnline = 8,
    UserBecameOffline = 9,
    MessageHasBeenRead = 10,
    MessageHasBeenAdded = 11,
    ChatParamsWereChanged = 12,
    MessageHasBeenDeleted = 20,
    UserIsTyping = 61,
    UserIsTypingInChat = 62,
    NewCounter = 80,
    NotificationSettingsChanged = 114,
    MessageHasBeenRestored = 1000000,
  }
}
