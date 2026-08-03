using System;
using VKClient.Common;
using VKClient.Common.Utils;

namespace VKClient.Audio.Base.Utils
{
  public static class AvatarHelper
  {
    // VeryLowProfileImageLoader disk-caches streams whose URL carries "RequireCache".
    public const string CACHE_KEY = "RequireCache";
    // ImageLoader.IsGifUri skips the animation path for URLs carrying "static",
    // so avatar GIFs are decoded by the native BitmapImage as plain images.
    public const string STATIC_KEY = "static";

    public static string Prepare(string url)
    {
      if (string.IsNullOrEmpty(url))
        return url;
      url = OpenVKInstanceManager.ResolveWebUrl(url);
      if (url.StartsWith("http://api.openvk.org", StringComparison.OrdinalIgnoreCase) ||
          (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && OpenVKInstanceManager.IsCurrentInstanceUrl(url)))
        url = "https://" + url.Substring("http://".Length);
      if (url.IndexOf('?') >= 0)
      {
        if (UriExtensions.ParseQueryString(url).ContainsKey(AvatarHelper.CACHE_KEY))
          return url;
        url += "&";
      }
      else
        url += "?";
      url += AvatarHelper.CACHE_KEY + "=1&" + AvatarHelper.STATIC_KEY + "=1";
      return url;
    }
  }
}
