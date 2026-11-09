using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using VKClient.Audio.Base.DataObjects;
using VKClient.Audio.Base.Library;
using VKClient.Common.Framework;
using VKClient.Common.Utils;

namespace VKClient.Common.Emoji
{
  public class StickersDownloader
  {
    private static StickersDownloader _instance;

    public static StickersDownloader Instance
    {
      get
      {
        if (StickersDownloader._instance == null)
          StickersDownloader._instance = new StickersDownloader();
        return StickersDownloader._instance;
      }
    }

    private StickersDownloader()
    {
    }

    public void InitiateDownload(StoreProduct storeProduct)
    {
      List<int> stickerIds = storeProduct.stickers.sticker_ids;
      string baseUrl = storeProduct.stickers.base_url;
      List<RemoteLocalMapping> downloadList = new List<RemoteLocalMapping>();
      foreach (int stickerId in stickerIds)
      {
        downloadList.Add(new RemoteLocalMapping()
        {
          RemoteUri = AssetUrl(storeProduct, stickerId, 256),
          LocalPath = CacheName(AssetUrl(storeProduct, stickerId, 256))
        });
        if (ScaleFactor.GetScaleFactor() == 100)
          downloadList.Add(new RemoteLocalMapping()
          {
            RemoteUri = AssetUrl(storeProduct, stickerId, 128),
            LocalPath = CacheName(AssetUrl(storeProduct, stickerId, 128))
          });
      }
      if (!storeProduct.base_url.TrimEnd('/').EndsWith("/images/stickers", StringComparison.OrdinalIgnoreCase))
        downloadList.Add(new RemoteLocalMapping()
      {
        RemoteUri = storeProduct.base_url + "/background.png",
        LocalPath = storeProduct.id.ToString() + "background.png"
      });
      BatchDownloadManager.GetDownloadManager(storeProduct.id.ToString(), downloadList).Start();
    }

    public string GetLocalPathForStickerId512(StoreProduct storeProduct, int stickerId)
    {
      return CachedOrRemote(AssetUrl(storeProduct, stickerId, 512));
    }

    public string GetLocalPathForStickerId256(StoreProduct storeProduct, int stickerId)
    {
      return CachedOrRemote(AssetUrl(storeProduct, stickerId, 256));
    }

    public string GetLocalPathForStickerId128(StoreProduct storeProduct, int stickerId)
    {
      return CachedOrRemote(AssetUrl(storeProduct, stickerId, ScaleFactor.GetScaleFactor() == 100 ? 128 : 256));
    }

    public string GetLocalPathForStickerId128(string baseUri, int stickerId)
    {
      if (ScaleFactor.GetScaleFactor() != 100)
        return this.GetLocalPathForStickerId256(baseUri, stickerId);
      return CachedOrRemote(baseUri + stickerId + "/128b.png");
    }

    public string GetLocalPathForStickerId256(string baseUri, int stickerId)
    {
      return CachedOrRemote(baseUri + stickerId + "/256b.png");
    }

    public string GetLocalPathForStickerId512(string baseUri, int stickerId)
    {
      return CachedOrRemote(baseUri + stickerId + "/512b.png");
    }

    private static string AssetUrl(StoreProduct product, int id, int pixels)
    {
      return product.is_animated ? OpenVKInstanceManager.ResolveWebUrl("/sticker/" + product.id + "/" + id + "_512.json") :
        product.stickers.base_url + id + "/" + pixels + "b.png";
    }

    private static string CacheName(string url)
    {
      // Stable and instance-specific; never use an HTTP URL as a storage path.
      ulong hash = 14695981039346656037UL;
      unchecked { foreach (char c in url) { hash ^= c; hash *= 1099511628211UL; } }
      return "OpenVKSticker_" + hash.ToString("x16") + (url.EndsWith(".json") ? ".json" : ".png");
    }

    private static string CachedOrRemote(string url)
    {
      string file = CacheName(url);
      using (var store = IsolatedStorageFile.GetUserStoreForApplication()) if (store.FileExists(file)) return file;
      return url;
    }
  }
}
