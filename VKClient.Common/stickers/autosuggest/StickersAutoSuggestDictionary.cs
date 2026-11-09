using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VKClient.Audio.Base.BackendServices;
using VKClient.Audio.Base.DataObjects;
using VKClient.Audio.Base.Events;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Emoji;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Stickers.ViewModels;

namespace VKClient.Common.Stickers.AutoSuggest
{
  public class StickersAutoSuggestDictionary : IBinarySerializable, IHandle<UserIsLoggedOutEvent>, IHandle,
    IHandle<StickersPackPurchasedEvent>, IHandle<StickersUpdatedEvent>, IHandle<StickersPackActivatedDeactivatedEvent>,
    IHandle<StickersPacksReorderedEvent>, IHandle<AutoSuggestEnabledChangedEvent>
  {
    private const string CacheFile = "StickersAutoSuggestData";
    private static StickersAutoSuggestDictionary _instance;
    private DateTime _lastLoadedDate = DateTime.MinValue, _retryAfter = DateTime.MinValue;
    private readonly Dictionary<string, StickerKeywordItem> _lookupDictionary = new Dictionary<string, StickerKeywordItem>();
    private readonly Dictionary<int, Sticker> _assets = new Dictionary<int, Sticker>();
    private StickersKeywordsData _keywordsData;
    private bool _isLoading, _isLoaded, _isRestoringState;
    private long _owner;
    private string _origin;

    public static StickersAutoSuggestDictionary Instance
    {
      get { return _instance ?? (_instance = new StickersAutoSuggestDictionary()); }
    }
    public StickersAutoSuggestDictionary() { EventAggregator.Current.Subscribe(this); }

    public string PrepareTextForLookup(string text)
    {
      if (string.IsNullOrWhiteSpace(text) || text.Length > 100) return "";
      text = text.ToLowerInvariant().Replace('ё', 'е').Replace("\ufe0f", "").Replace("\ufe0e", "");
      for (int modifier = 0x1f3fb; modifier <= 0x1f3ff; modifier++)
        text = text.Replace(char.ConvertFromUtf32(modifier), "");
      return Regex.Replace(text.Trim().TrimEnd('.', ',', '!', '?', ':', ';'), @"\s+", " ").Trim();
    }

    public IEnumerable<StickersAutoSuggestItem> GetAutoSuggestItemsFor(string text)
    {
      var result = new List<StickersAutoSuggestItem>();
      if (!AppGlobalStateManager.Current.GlobalState.StickersAutoSuggestEnabled || !_isLoaded || _isRestoringState || !IsCurrentAccount()) return result;
      text = PrepareTextForLookup(text);
      StickerKeywordItem group;
      if (!_lookupDictionary.TryGetValue(text, out group))
      {
        int space = text.LastIndexOf(' ');
        if (space < 0 || !_lookupDictionary.TryGetValue(text.Substring(space + 1), out group)) return result;
      }
      foreach (int id in SortBasedOnRecents(group.user_stickers))
      {
        StickerItemData data = CreateItem(id);
        if (data != null) result.Add(new StickersAutoSuggestItem(data, false));
        if (result.Count == 20) break;
      }
      return result;
    }

    private StickerItemData CreateItem(int id)
    {
      Sticker sticker;
      if (_assets.TryGetValue(id, out sticker))
      {
        bool animated = (sticker.photo_256 ?? "").EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        string small = animated ? sticker.photo_256 : sticker.photo_128 ?? sticker.photo_256;
        if (string.IsNullOrEmpty(small)) return null;
        return new StickerItemData { StickerId = id, StickerSetId = sticker.product_id, CanSendSticker = true,
          LocalPath = small, LocalPathBig = sticker.photo_256 ?? small, LocalPathExtraBig = sticker.photo_256 ?? small };
      }
      // Old instances may return numeric IDs. Use an owned keyboard pack,
      // never a fabricated URL or a sticker that requires purchase.
      var stocks = StickersSettings.Instance.StickersList;
      var product = stocks == null ? null : stocks.Where(s => s != null && s.product != null && s.product.purchased != 0)
        .Select(s => s.product).FirstOrDefault(p => p.stickers != null && p.stickers.sticker_ids != null && p.stickers.sticker_ids.Contains(id));
      if (product == null) return null;
      return new StickerItemData { StickerId = id, StickerSetId = product.id, CanSendSticker = true,
        LocalPath = StickersDownloader.Instance.GetLocalPathForStickerId128(product, id),
        LocalPathBig = StickersDownloader.Instance.GetLocalPathForStickerId256(product, id),
        LocalPathExtraBig = StickersDownloader.Instance.GetLocalPathForStickerId512(product, id) };
    }

    private IEnumerable<int> SortBasedOnRecents(List<int> ids)
    {
      ids = ids ?? new List<int>();
      var recent = StickersSettings.Instance.RecentStickers;
      return (recent == null || recent.sticker_ids == null ? Enumerable.Empty<int>() : recent.sticker_ids.Where(ids.Contains))
        .Concat(ids).Where(id => id > 0).Distinct();
    }

    private bool IsCurrentAccount()
    {
      return _owner != 0 && _owner == AppGlobalStateManager.Current.LoggedInUserId && _origin == OpenVKInstanceManager.Current.ApiOrigin;
    }
    public void EnsureDictIsLoadedAndUpToDate(bool forceReload = false)
    {
      if (_isLoading || _isRestoringState || AppGlobalStateManager.Current.LoggedInUserId == 0 ||
          !AppGlobalStateManager.Current.GlobalState.StickersAutoSuggestEnabled || DateTime.UtcNow < _retryAfter) return;
      if (!forceReload && IsCurrentAccount() && _isLoaded && (DateTime.UtcNow - _lastLoadedDate).TotalHours < 12) return;
      long owner = AppGlobalStateManager.Current.LoggedInUserId;
      string origin = OpenVKInstanceManager.Current.ApiOrigin;
      _isLoading = true;
      StoreService.Instance.GetStickersKeywords(res => Execute.ExecuteOnUIThread(() => {
        _isLoading = false;
        if (_instance != this || owner != AppGlobalStateManager.Current.LoggedInUserId || origin != OpenVKInstanceManager.Current.ApiOrigin) return;
        if (res.ResultCode == ResultCode.Succeeded && res.ResultData != null && res.ResultData.dictionary != null)
        {
          _owner = owner; _origin = origin; SetKeywordsData(res.ResultData);
          _lastLoadedDate = DateTime.UtcNow; _isLoaded = true;
          SaveState(); PublishUpdated();
        }
        else _retryAfter = DateTime.UtcNow.AddMinutes(1);
      }));
    }

    private void SetKeywordsData(StickersKeywordsData data)
    {
      _keywordsData = data; _lookupDictionary.Clear(); _assets.Clear();
      if (data == null) return;
      foreach (var sticker in data.stickers ?? new List<Sticker>())
        if (sticker != null && sticker.id > 0 && sticker.id <= int.MaxValue) _assets[(int)sticker.id] = sticker;
      foreach (var item in data.dictionary ?? new List<StickerKeywordItem>())
      {
        if (item == null) continue;
        foreach (string word in item.words ?? new List<string>())
        {
          string key = PrepareTextForLookup(word);
          if (key.Length == 0) continue;
          StickerKeywordItem group;
          if (!_lookupDictionary.TryGetValue(key, out group)) _lookupDictionary[key] = group = new StickerKeywordItem();
          group.user_stickers = group.user_stickers.Concat(item.user_stickers ?? new List<int>()).Distinct().ToList();
        }
      }
    }

    private void PublishUpdated() { EventAggregator.Current.Publish(new AutoSuggestDictionaryUpdatedEvent()); }
    public void SaveState() { if (_isLoaded && IsCurrentAccount()) CacheManager.TrySerialize(this, CacheFile, false, CacheManager.DataType.CachedData); }
    public async void RestoreStateAsync()
    {
      _isRestoringState = true;
      try { await CacheManager.TryDeserializeAsync(this, CacheFile, CacheManager.DataType.CachedData); }
      finally { _isRestoringState = false; }
      if (_instance != this) return;
      if (IsCurrentAccount()) PublishUpdated();
      EnsureDictIsLoadedAndUpToDate();
    }
    public void Write(BinaryWriter writer)
    {
      writer.Write(2); writer.Write(_isLoaded); BinarySerializerExtensions.Write(writer, _lastLoadedDate);
      writer.Write<StickersKeywordsData>(_keywordsData, false); writer.Write(_owner); writer.WriteString(_origin);
    }
    public void Read(BinaryReader reader)
    {
      int version = reader.ReadInt32(); _isLoaded = reader.ReadBoolean();
      _lastLoadedDate = BinarySerializerExtensions.ReadDateTime(reader);
      SetKeywordsData(reader.ReadGeneric<StickersKeywordsData>());
      _owner = version >= 2 ? reader.ReadInt64() : 0; _origin = version >= 2 ? reader.ReadString() : "";
      if (!IsCurrentAccount()) { _isLoaded = false; _lastLoadedDate = DateTime.MinValue; }
    }
    public void Handle(UserIsLoggedOutEvent message)
    {
      if (_instance != this) return;
      EventAggregator.Current.Unsubscribe(this); _instance = null;
      _isLoaded = false; SetKeywordsData(null); PublishUpdated();
    }
    public void Handle(StickersPackPurchasedEvent message) { Refresh(); }
    public void Handle(StickersUpdatedEvent message) { Refresh(); }
    public void Handle(StickersPackActivatedDeactivatedEvent message) { Refresh(); }
    public void Handle(StickersPacksReorderedEvent message) { PublishUpdated(); }
    public void Handle(AutoSuggestEnabledChangedEvent message) { Refresh(); PublishUpdated(); }
    private void Refresh() { if (_instance == this) { _lastLoadedDate = DateTime.MinValue; EnsureDictIsLoadedAndUpToDate(true); } }
    public class AutoSuggestDictionaryUpdatedEvent { }
  }
}
