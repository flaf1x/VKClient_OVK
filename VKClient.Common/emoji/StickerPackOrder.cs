using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Linq;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Library;

namespace VKClient.Common.Emoji
{
    // OpenVK has no reorderProducts API. Keep the keyboard order locally,
    // separately for each instance/account, instead of calling a missing method.
    public static class StickerPackOrder
    {
        private static string Key { get { return "OpenVK.StickerOrder." + OpenVKInstanceManager.Current.WebOrigin + "." + AppGlobalStateManager.Current.LoggedInUserId; } }
        public static void Save(IEnumerable<int> ids)
        {
            var settings = IsolatedStorageSettings.ApplicationSettings;
            settings[Key] = ids.ToList(); settings.Save();
        }
        public static List<StockItem> Apply(IEnumerable<StockItem> items)
        {
            List<int> order;
            if (!IsolatedStorageSettings.ApplicationSettings.TryGetValue(Key, out order) || order == null) return items.ToList();
            return items.OrderBy(item => { int index = item.product == null ? -1 : order.IndexOf(item.product.id); return index < 0 ? int.MaxValue : index; }).ToList();
        }
    }
}
