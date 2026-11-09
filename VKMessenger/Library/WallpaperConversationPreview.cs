using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Localization;
using VKMessenger.Backend;
using VKMessenger.Library.VirtItems;

namespace VKMessenger.Library
{
    // Use actual message/footer/sticker renderers without duplicate bubble
    // markup. Synthetic rows never enter a conversation/cache.
    public sealed class WallpaperConversationPreview : IDisposable
    {
        private readonly List<MessageItem> _rows = new List<MessageItem>();
        private WallpaperConversationPreview(Panel host, bool wallpaper)
        {
            Add(host, new Message { body = CommonResources.ChatBackgroundPreviewIncoming }, wallpaper);
            Add(host, new Message { body = CommonResources.ChatBackgroundPreviewOutgoing, @out = 1 }, wallpaper);
            // First sticker from the OpenVK "Беббер и Вермишель" pack.
            Add(host, new Message { @out = 1, attachments = new List<Attachment> { new Attachment {
                type = "sticker", sticker = new Sticker { id = 24, product_id = 4, width = 256, height = 256,
                    photo_256 = OpenVKInstanceManager.Current.ApiOrigin.TrimEnd('/') + "/images/stickers/24/256b.png" } } } }, wallpaper);
        }
        private void Add(Panel host, Message message, bool wallpaper)
        {
            message.mid = 1; message.read_state = 1;
            message.date = VKClient.Common.Utils.Extensions.DateTimeToUnixTimestamp(DateTime.UtcNow, true);
            // uid=0 deliberately avoids user/profile requests for synthetic rows.
            var model = new MessageViewModel(message) { UIDate = "12:34" };
            var row = new MessageItem(model);
            row.View.HorizontalAlignment = message.@out == 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            row.ChangeState(VirtualizableState.LoadedFully);
            host.Children.Add(row.View); _rows.Add(row);
        }
        public static IDisposable Create(Panel host, bool wallpaper) { return new WallpaperConversationPreview(host, wallpaper); }
        public void Dispose()
        {
            foreach (var row in _rows) row.ChangeState(VirtualizableState.Unloaded);
            _rows.Clear();
        }
    }
}
