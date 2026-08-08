using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VKClient.Common.Utils;

namespace VKClient.Common.Backend
{
  /// <summary>
  /// Converts current OpenVK response shapes to the legacy VK API 5.64
  /// structures used by the Windows Phone client.
  /// </summary>
  public static class OpenVKResponseAdapter
  {
    public static string Adapt(string methodName, string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return json;

      try
      {
        JToken root = JToken.Parse(json);
        NormalizeToken(root);
        NormalizeMethodResponse(methodName, root as JObject);
        return root.ToString(Formatting.None);
      }
      catch (Exception ex)
      {
        // Never log the response body here: it can contain private messages.
        Logger.Instance.Error("OpenVK response adaptation failed for method " + (methodName ?? ""), ex);
        return json;
      }
    }

    private static void NormalizeToken(JToken token)
    {
      if (token == null)
        return;

      JObject obj = token as JObject;
      if (obj != null)
      {
        // Work on a snapshot because video normalization replaces image[].
        foreach (JProperty property in obj.Properties().ToList())
        {
          NormalizeToken(property.Value);
          if (property.Value.Type == JTokenType.Boolean &&
              IsLegacyIntegerBoolean(property.Name))
            property.Value = property.Value.Value<bool>() ? 1 : 0;
        }

        NormalizeAlbumCover(obj);
        NormalizePhoto(obj);
        NormalizeVideo(obj);
        NormalizePoll(obj);
        NormalizeCommonAliases(obj);
        return;
      }

      JArray array = token as JArray;
      if (array != null)
      {
        foreach (JToken child in array.ToList())
          NormalizeToken(child);
      }
    }

    private static void NormalizePhoto(JObject obj)
    {
      JArray sizes = obj["sizes"] as JArray;
      if (sizes == null)
        return;

      // "sizes" is used by photos. Requiring a photo identity prevents an
      // unrelated array with the same name from being rewritten.
      if (obj["id"] == null || (obj["album_id"] == null && obj["owner_id"] == null))
        return;
      if (obj["thumb_id"] != null && obj["title"] != null)
        return;

      // OpenVK stores some small resized copies as GIF. Skip those for normal
      // photos and choose the next bounded JPEG instead of expanding a tiny
      // preview URL back to the unrestricted original image.
      string thumb = FindStaticPhotoUrl(sizes, "r", "x", "q", "p", "o", "s", "y", "z", "w", "UPLOADED_MAXRES");
      string small = FindStaticPhotoUrl(sizes, "r", "x", "q", "p", "o", "s", "y", "z", "w", "UPLOADED_MAXRES") ?? thumb;
      string medium = FindStaticPhotoUrl(sizes, "x", "r", "y", "q", "p", "z", "o", "s", "w", "UPLOADED_MAXRES") ?? small;
      // photo_807/src_xbig is the field used by the full-screen WP viewer.
      // Prefer a bounded JPEG: original w/UPLOADED_MAXRES images can exhaust
      // the Lumia's decoder memory even when the UI only needs ~800 pixels.
      string large = FindStaticPhotoUrl(sizes, "y", "x", "r", "z", "q", "p", "o", "s", "w", "UPLOADED_MAXRES") ?? medium;
      string xlarge = FindStaticPhotoUrl(sizes, "z", "y", "x", "r", "w", "UPLOADED_MAXRES") ?? large;
      // The WP viewer cannot keep several unrestricted 8K bitmaps alive.
      // OpenVK's w variant is the high-quality source capped at 2560x2048.
      string original = FindStaticPhotoUrl(sizes, "w", "z", "UPLOADED_MAXRES", "y", "x", "r") ?? xlarge;

      SetUrl(obj, "photo_75", thumb);
      SetUrl(obj, "src_small", thumb);
      SetUrl(obj, "photo_130", small);
      SetUrl(obj, "src", small);
      SetUrl(obj, "photo_604", medium);
      SetUrl(obj, "src_big", medium);
      SetUrl(obj, "photo_807", large);
      SetUrl(obj, "src_xbig", large);
      SetUrl(obj, "photo_1280", xlarge);
      SetUrl(obj, "src_xxbig", xlarge);
      SetUrl(obj, "photo_2560", original);

      List<ImageSize> candidates = ReadImageSizes(sizes);
      if (candidates.Count == 0)
        return;

      ImageSize largest = candidates
        .OrderByDescending(s => Math.Max(s.Width, s.Height))
        .First();
      if (GetPositiveInt(obj["width"]) == 0)
        obj["width"] = largest.Width;
      if (GetPositiveInt(obj["height"]) == 0)
        obj["height"] = largest.Height;
    }

    private static void NormalizeAlbumCover(JObject obj)
    {
      JArray sizes = obj["sizes"] as JArray;
      if (sizes == null || obj["thumb_id"] == null || obj["title"] == null)
        return;

      // photos.getAlbums already exposes the cover sizes. The old client
      // tried to rebuild cover ids and called photos.getById with OpenVK's
      // internal thumb_id, so a part of the covers was always missing.
      string cover = FindStaticPhotoUrl(sizes, "x", "r", "y", "z", "w", "UPLOADED_MAXRES");
      string small = FindStaticPhotoUrl(sizes, "r", "x", "y", "z", "w", "UPLOADED_MAXRES") ?? cover;
      if (!string.IsNullOrEmpty(cover))
        obj["thumb_src"] = UpgradeOpenVkImageToHttps(cover);
      if (!string.IsNullOrEmpty(small))
        obj["thumb_src_small"] = UpgradeOpenVkImageToHttps(small);
    }

    private static void NormalizeVideo(JObject obj)
    {
      JArray images = obj["image"] as JArray;
      if (images == null || obj["id"] == null || obj["owner_id"] == null)
        return;

      // OpenVK uses [] when an uploaded video has no directly playable files.
      // The legacy Video model expects Dictionary<string, string>, so leaving
      // the empty array aborts deserialization of the whole feed page.
      JToken files = obj["files"];
      if (files is JArray)
        obj["files"] = new JObject();

      if (IsNullOrMissing(obj["duration"]))
        obj["duration"] = 0;

      List<ImageSize> candidates = ReadImageSizes(images);
      if (candidates.Count == 0)
      {
        // A JArray cannot be assigned to the legacy string Video.image.
        obj.Remove("image");
        return;
      }

      ImageSize small = SelectForWidth(candidates, 130);
      ImageSize medium = SelectForWidth(candidates, 320);
      ImageSize large = SelectForWidth(candidates, 640);
      SetUrlIfMissing(obj, "photo_130", small);
      SetUrlIfMissing(obj, "photo_320", medium);
      SetUrlIfMissing(obj, "photo_640", large);
      SetUrlIfMissing(obj, "image_medium", medium);
      SetUrlIfMissing(obj, "image_big", large);

      // Replace the modern array because Video.image is a string alias for
      // photo_130 in the legacy model.
      obj["image"] = small.Url;
    }

    private static void NormalizePoll(JObject obj)
    {
      // OpenVK emits modern boolean poll flags. The WP model uses integer
      // flags, and Newtonsoft.Json 9 otherwise aborts the entire newsfeed on
      // the first "anonymous": false value.
      if (obj["question"] == null || !(obj["answers"] is JArray))
        return;

      JToken anonymous = obj["anonymous"];
      if (anonymous != null && anonymous.Type == JTokenType.Boolean)
        obj["anonymous"] = anonymous.Value<bool>() ? 1 : 0;

      JToken closed = obj["closed"];
      if (closed != null)
      {
        if (closed.Type == JTokenType.Boolean)
          obj["is_closed"] = closed.Value<bool>() ? 1 : 0;
        else
          obj["is_closed"] = GetLong(closed) != 0L ? 1 : 0;
      }

      if (!(obj["answer_ids"] is JArray))
        obj["answer_ids"] = new JArray();
    }

    private static void NormalizeCommonAliases(JObject obj)
    {
      NormalizeAvatarUrls(obj);

      // VepurOVK returns city/country as plain strings while VK API 5.64
      // exposes { id, title } objects. The profile model cannot deserialize
      // a string directly and would discard the whole users.get response.
      NormalizeLocationObject(obj, "city");
      NormalizeLocationObject(obj, "country");

      // VepurOVK serializes tracks and photos outside an album with a JSON
      // null. Legacy models use a non-nullable Int64 and otherwise reject the
      // entire response collection.
      if (obj["album_id"] != null && obj["album_id"].Type == JTokenType.Null)
        obj["album_id"] = 0;
      // OpenVK may expose an audio album id as "ownerId_albumId". The
      // legacy AudioObj model stores album_id as Int64, so one such track
      // would otherwise abort deserialization of the entire audio.get page.
      if (obj["album_id"] != null &&
          obj["album_id"].Type == JTokenType.String &&
          obj["artist"] != null && obj["title"] != null)
      {
        string albumId = obj["album_id"].Value<string>();
        int separator = string.IsNullOrEmpty(albumId) ? -1 : albumId.LastIndexOf('_');
        long numericAlbumId;
        string numericPart = separator >= 0 && separator + 1 < albumId.Length
          ? albumId.Substring(separator + 1)
          : albumId;
        obj["album_id"] = long.TryParse(numericPart, out numericAlbumId)
          ? numericAlbumId
          : 0L;
      }
      if (obj["aid"] != null && obj["aid"].Type == JTokenType.Null)
        obj["aid"] = 0;

      // Audio is still being processed when OpenVK returns url:false. The
      // legacy model stores url as a string, so keep the track deserializable.
      if (obj["artist"] != null && obj["title"] != null &&
          (obj["url"] == null || obj["url"].Type == JTokenType.Null ||
           (obj["url"].Type == JTokenType.Boolean && !obj["url"].Value<bool>())))
        obj["url"] = string.Empty;

      // Some OpenVK forks (including VepurOVK) expose modern wall/feed ids
      // only. The legacy WP feed resolves the author exclusively through
      // source_id and opens the post through post_id.
      if (obj["post_type"] != null && obj["id"] != null)
      {
        if (IsNullOrMissing(obj["source_id"]) || GetLong(obj["source_id"]) == 0L)
        {
          long sourceId = GetLong(obj["owner_id"]);
          if (sourceId == 0L)
            sourceId = GetLong(obj["from_id"]);
          if (sourceId != 0L)
            obj["source_id"] = sourceId;
        }
        if (IsNullOrMissing(obj["post_id"]) || GetLong(obj["post_id"]) == 0L)
          obj["post_id"] = obj["id"].DeepClone();
      }

      // OpenVK uses "url" in modern photo/document size objects, while the
      // legacy document preview model only knows "src".
      if (IsEmptyString(obj["src"]) &&
          !IsEmptyString(obj["url"]))
        obj["src"] = obj["url"].DeepClone();

      // Preview sizes in docs/photos may have unknown dimensions. The legacy
      // client models use non-nullable integers, so preserve the item with 0.
      if ((!IsEmptyString(obj["src"]) || !IsEmptyString(obj["url"])))
      {
        if (obj["width"] != null && obj["width"].Type == JTokenType.Null)
          obj["width"] = 0;
        if (obj["height"] != null && obj["height"].Type == JTokenType.Null)
          obj["height"] = 0;
      }

      if (IsEmptyString(obj["photo_max"]))
      {
        string photo = GetString(obj["photo_200"]);
        if (string.IsNullOrEmpty(photo))
          photo = GetString(obj["photo_100"]);
        if (string.IsNullOrEmpty(photo))
          photo = GetString(obj["photo_50"]);
        if (!string.IsNullOrEmpty(photo))
          obj["photo_max"] = photo;
      }

      // The old profile UI displays "activity", while OpenVK exposes the
      // user's text status in "status".
      if (IsEmptyString(obj["activity"]) && !IsEmptyString(obj["status"]))
        obj["activity"] = obj["status"].DeepClone();

      // Message fields used by the old client. OpenVK already emits both for
      // v=5.64 in most handlers, but long poll/history payloads are not uniform.
      if (obj["peer_id"] != null || obj["from_id"] != null)
      {
        if (IsNullOrMissing(obj["body"]) && !IsNullOrMissing(obj["text"]))
          obj["body"] = obj["text"].DeepClone();
        if (IsNullOrMissing(obj["text"]) && !IsNullOrMissing(obj["body"]))
          obj["text"] = obj["body"].DeepClone();
        long peerId = GetLong(obj["peer_id"]);
        long userId = GetLong(obj["user_id"]);
        if (IsNullOrMissing(obj["user_id"]) || userId == 0L)
        {
          // OpenVK currently emits user_id=0 in message history and
          // conversations. For direct dialogs the peer itself is the user.
          if (peerId != 0L && (peerId < 2000000000L || peerId >= 3000000000L))
            obj["user_id"] = peerId;
          else if (!IsNullOrMissing(obj["from_id"]))
            obj["user_id"] = obj["from_id"].DeepClone();
        }
        if (peerId >= 2000000000L && peerId < 3000000000L &&
            (IsNullOrMissing(obj["chat_id"]) || GetLong(obj["chat_id"]) == 0L))
          obj["chat_id"] = peerId - 2000000000L;
        if (IsNullOrMissing(obj["attachments"]))
          obj["attachments"] = new JArray();
        if (IsNullOrMissing(obj["fwd_messages"]))
          obj["fwd_messages"] = new JArray();
        if (IsNullOrMissing(obj["push_settings"]))
          obj["push_settings"] = new JObject(new JProperty("disabled_until", 0));
      }

      // The old model stores the inverse of OpenVK's is_processed flag.
      if (obj["files"] is JObject && obj["owner_id"] != null && obj["id"] != null &&
          IsNullOrMissing(obj["processing"]) && !IsNullOrMissing(obj["is_processed"]))
        obj["processing"] = GetLong(obj["is_processed"]) == 1L ? 0 : 1;
    }

    private static void NormalizeLocationObject(JObject obj, string propertyName)
    {
      JToken value = obj[propertyName];
      if (value == null || value.Type != JTokenType.String)
        return;
      string title = value.Value<string>() ?? "";
      obj[propertyName] = new JObject(
        new JProperty("id", 0),
        new JProperty("title", title),
        new JProperty("name", title));
    }

    private static void NormalizeAvatarUrls(JObject obj)
    {
      string[] avatarFields =
      {
        "photo_max", "photo_200", "photo_100", "photo_50",
        "photo_big", "photo_medium", "photo_medium_rec", "photo_rec", "photo"
      };

      string avatarUrl = avatarFields
        .Select(field => GetString(obj[field]))
        .FirstOrDefault(IsHttpUrl);
      if (string.IsNullOrEmpty(avatarUrl))
        return;

      foreach (string field in avatarFields)
      {
        string value = GetString(obj[field]);
        if (!string.IsNullOrEmpty(value))
          obj[field] = UpgradeOpenVkImageToHttps(value);
      }

      string photo50 = GetString(obj["photo_50"]);
      string photo100 = GetString(obj["photo_100"]);
      string photo200 = GetString(obj["photo_200"]);
      string compactAvatar = GetString(obj["photo_medium_rec"]);
      if (string.IsNullOrEmpty(compactAvatar))
        compactAvatar = GetString(obj["photo"]);

      // Preserve the server-provided size variants. AvatarHelper marks their
      // GIF URLs as static, so BitmapImage displays the first frame without
      // running the animated GIF decoder.
      if (string.IsNullOrEmpty(photo50))
        photo50 = !string.IsNullOrEmpty(compactAvatar)
          ? compactAvatar
          : BuildOpenVkCroppedVariant(avatarUrl, "miniscule", "gif");
      if (string.IsNullOrEmpty(photo100))
        photo100 = !string.IsNullOrEmpty(compactAvatar)
          ? compactAvatar
          : BuildOpenVkCroppedVariant(avatarUrl, "tiny", "gif");
      if (string.IsNullOrEmpty(photo200))
        photo200 = BuildOpenVkCroppedVariant(avatarUrl, "normal", "jpeg");

      // Some VepurOVK responses mix a stable media-CDN photo_200/photo_max
      // with photo_50/photo_100 links on the API host. Those thumbnail links
      // intermittently fail in Silverlight's image loader. Prefer the JPEG
      // media variant for compact controls as well; it is decoded once and
      // shared by the existing avatar cache.
      string stableAvatar = IsStableMediaAvatar(photo200)
        ? photo200
        : (IsStableMediaAvatar(avatarUrl) ? avatarUrl : null);
      if (!string.IsNullOrEmpty(stableAvatar))
      {
        if (!IsStableMediaAvatar(photo50))
          photo50 = stableAvatar;
        if (!IsStableMediaAvatar(photo100))
          photo100 = stableAvatar;
      }

      SetUrl(obj, "photo_50", photo50);
      SetUrl(obj, "photo_rec", photo50);
      SetUrl(obj, "photo_100", photo100);
      SetUrl(obj, "photo_medium", photo100);
      SetUrl(obj, "photo_200", photo200);
      SetUrl(obj, "photo_big", photo200);

      // Legacy controls use photo_max even for a 30-60 px avatar, therefore
      // keep that alias bounded as well.
      string legacyMax = !string.IsNullOrEmpty(photo200)
        ? photo200
        : (!string.IsNullOrEmpty(photo100) ? photo100 : avatarUrl);
      SetUrl(obj, "photo_max", legacyMax);

    }

    private static bool IsStableMediaAvatar(string url)
    {
      if (!IsHttpUrl(url))
        return false;
      Uri uri;
      if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        return false;
      return uri.Host.StartsWith("media.", StringComparison.OrdinalIgnoreCase) ||
             uri.AbsolutePath.IndexOf("/content/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildOpenVkCroppedVariant(string url, string sizeName, string extension)
    {
      if (!IsHttpUrl(url))
        return null;

      url = UpgradeOpenVkImageToHttps(url);
      Uri uri;
      if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        return null;

      string withoutQuery = url;
      string query = "";
      int queryIndex = withoutQuery.IndexOf('?');
      if (queryIndex >= 0)
      {
        query = withoutQuery.Substring(queryIndex);
        withoutQuery = withoutQuery.Substring(0, queryIndex);
      }

      int croppedIndex = withoutQuery.LastIndexOf("_cropped/", StringComparison.OrdinalIgnoreCase);
      if (croppedIndex >= 0)
        return withoutQuery.Substring(0, croppedIndex + "_cropped/".Length) +
               sizeName + "." + extension + query;

      int jpegIndex = withoutQuery.LastIndexOf(".jpeg", StringComparison.OrdinalIgnoreCase);
      if (jpegIndex >= 0)
        return withoutQuery.Substring(0, jpegIndex) + "_cropped/" +
               sizeName + "." + extension + query;

      return null;
    }

    private static string ConvertOpenVkResizedGifToOriginalJpeg(string url)
    {
      if (!IsHttpUrl(url))
        return null;

      string lower = url.ToLowerInvariant();
      int croppedIndex = lower.LastIndexOf("_cropped/", StringComparison.Ordinal);
      if (croppedIndex < 0)
        return null;

      int gifIndex = lower.IndexOf(".gif", croppedIndex, StringComparison.Ordinal);
      if (gifIndex < 0)
        return null;

      return url.Substring(0, croppedIndex) + ".jpeg" + url.Substring(gifIndex + 4);
    }

    private static void NormalizeMethodResponse(string methodName, JObject root)
    {
      if (root == null)
        return;

      if (root["response"] != null && root["response"].Type == JTokenType.Boolean)
        root["response"] = root["response"].Value<bool>() ? 1 : 0;

      JObject response = root["response"] as JObject;
      if (response == null)
        return;

      string method = (methodName ?? "").ToLowerInvariant();
      if (method == "execute.getuserinfo")
      {
        JObject newsfeed = response["newsfeed"] as JObject;
        if (newsfeed != null)
        {
          JObject lists = newsfeed["lists"] as JObject;
          if (lists == null)
          {
            lists = new JObject();
            newsfeed["lists"] = lists;
          }
          EnsureArray(lists, "sections");
          EnsureArray(lists, "lists");
        }
      }
      else if (method == "newsfeed.get" ||
          method == "newsfeed.getglobal")
      {
        EnsureArray(response, "items");
        EnsureArray(response, "profiles");
        EnsureArray(response, "groups");
        if (response["count"] == null)
          response["count"] = ((JArray)response["items"]).Count;
      }
      else if (method == "wall.get" ||
               method == "wall.getbyid")
      {
        EnsureArray(response, "items");
        EnsureArray(response, "profiles");
        EnsureArray(response, "groups");
        if (response["count"] == null)
          response["count"] = ((JArray)response["items"]).Count;
      }
      else if (method == "messages.gethistory" ||
               method == "messages.getbyid")
      {
        EnsureArray(response, "items");
        EnsureArray(response, "profiles");
        EnsureArray(response, "groups");
        EnsureArray(response, "conversations");
      }
      else if (method == "messages.getconversations" ||
               method == "messages.getconversationsbyid")
      {
        EnsureArray(response, "items");
        EnsureArray(response, "profiles");
        EnsureArray(response, "groups");
      }
      else if (method == "notifications.get")
      {
        EnsureArray(response, "items");
        EnsureArray(response, "profiles");
        EnsureArray(response, "groups");
      }
      else if (method == "photos.getall" ||
               method == "photos.get" ||
               method == "video.get" ||
               method == "docs.get" ||
               method == "friends.getrequests")
      {
        EnsureArray(response, "items");
        if (method == "video.get")
        {
          EnsureArray(response, "profiles");
          EnsureArray(response, "groups");
        }
        if (response["count"] == null)
          response["count"] = ((JArray)response["items"]).Count;
      }
    }

    private static void EnsureArray(JObject obj, string propertyName)
    {
      JToken value = obj[propertyName];
      if (value == null || value.Type == JTokenType.Null ||
          value.Type == JTokenType.Boolean && !value.Value<bool>())
        obj[propertyName] = new JArray();
    }

    private static List<ImageSize> ReadImageSizes(JArray array)
    {
      List<ImageSize> result = new List<ImageSize>();
      foreach (JObject item in array.OfType<JObject>())
      {
        string url = GetString(item["url"]);
        if (string.IsNullOrEmpty(url))
          url = GetString(item["src"]);

        int width = GetPositiveInt(item["width"]);
        int height = GetPositiveInt(item["height"]);
        if (!IsHttpUrl(url) || width <= 0 || height <= 0)
          continue;

        result.Add(new ImageSize(UpgradeOpenVkImageToHttps(url), width, height));
      }
      return result;
    }

    private static ImageSize SelectForWidth(List<ImageSize> candidates, int target)
    {
      ImageSize result = candidates
        .Where(s => Math.Max(s.Width, s.Height) >= target)
        .OrderBy(s => Math.Max(s.Width, s.Height))
        .FirstOrDefault();
      if (result != null)
        return result;

      return candidates
        .OrderByDescending(s => Math.Max(s.Width, s.Height))
        .First();
    }

    private static void SetUrlIfMissing(JObject obj, string propertyName, ImageSize size)
    {
      if (size != null && IsEmptyString(obj[propertyName]))
        obj[propertyName] = size.Url;
    }

    private static void SetUrl(JObject obj, string propertyName, string url)
    {
      if (!string.IsNullOrEmpty(url))
        obj[propertyName] = UpgradeOpenVkImageToHttps(url);
    }

    private static string FindStaticPhotoUrl(JArray sizes, params string[] preferredTypes)
    {
      // Prefer a JPEG/PNG derivative: it is cheaper for the feed and is
      // understood by every WP image path. Some OpenVK photos only have GIF
      // derivatives though, so keep a static first-frame fallback instead of
      // returning null and leaving a permanently empty tile.
      foreach (string preferredType in preferredTypes)
      {
        foreach (JObject item in sizes.OfType<JObject>())
        {
          if (!string.Equals(GetString(item["type"]), preferredType, StringComparison.OrdinalIgnoreCase))
            continue;
          if (!HasUsablePhotoSize(item))
            continue;

          string url = GetString(item["url"]);
          if (string.IsNullOrEmpty(url))
            url = GetString(item["src"]);
          string staticUrl = GetStaticImageUrl(url);
          if (!string.IsNullOrEmpty(staticUrl))
            return staticUrl;
        }
      }

      foreach (string preferredType in preferredTypes)
      {
        foreach (JObject item in sizes.OfType<JObject>())
        {
          if (!string.Equals(GetString(item["type"]), preferredType, StringComparison.OrdinalIgnoreCase))
            continue;
          if (!HasUsablePhotoSize(item))
            continue;

          string url = GetString(item["url"]);
          if (string.IsNullOrEmpty(url))
            url = GetString(item["src"]);
          if (IsHttpUrl(url))
            return AddStaticImageMarker(UpgradeOpenVkImageToHttps(url));
        }
      }
      return null;
    }

    private static bool HasUsablePhotoSize(JObject item)
    {
      // Older OpenVK rows expose placeholder api.openvk.org thumbnail URLs
      // with explicit 0x0 dimensions. Those endpoints currently answer with
      // HTTP 308 HTML, while valid CDN variants are present in the same array.
      bool hasDimensions = item["width"] != null || item["height"] != null;
      return !hasDimensions ||
             (GetPositiveInt(item["width"]) > 0 && GetPositiveInt(item["height"]) > 0);
    }

    private static string FindPhotoUrl(JArray sizes, params string[] preferredTypes)
    {
      foreach (string preferredType in preferredTypes)
      {
        foreach (JObject item in sizes.OfType<JObject>())
        {
          if (!string.Equals(GetString(item["type"]), preferredType, StringComparison.OrdinalIgnoreCase))
            continue;

          string url = GetString(item["url"]);
          if (string.IsNullOrEmpty(url))
            url = GetString(item["src"]);
          if (IsHttpUrl(url))
            return UpgradeOpenVkImageToHttps(url);
        }
      }
      return null;
    }

    private static string AddStaticImageMarker(string url)
    {
      if (string.IsNullOrEmpty(url) ||
          url.IndexOf("static=", StringComparison.OrdinalIgnoreCase) >= 0)
        return url;
      return url + (url.IndexOf('?') >= 0 ? "&" : "?") + "static=1";
    }

    private static string GetStaticImageUrl(string url)
    {
      if (!IsHttpUrl(url))
        return null;

      url = UpgradeOpenVkImageToHttps(url);
      string path = url;
      int queryIndex = path.IndexOf('?');
      if (queryIndex >= 0)
        path = path.Substring(0, queryIndex);
      if (!path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        return url;

      return AddStaticImageMarker(url);
    }

    private static string UpgradeOpenVkImageToHttps(string url)
    {
      url = OpenVKInstanceManager.ResolveWebUrl(url);
      if (string.IsNullOrEmpty(url) || !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        return url;

      string hostAndPath = url.Substring("http://".Length);
      if (OpenVKInstanceManager.IsCurrentInstanceUrl(url) ||
          hostAndPath.StartsWith("openvk.org/", StringComparison.OrdinalIgnoreCase) ||
          hostAndPath.StartsWith("api.openvk.org/", StringComparison.OrdinalIgnoreCase) ||
          hostAndPath.StartsWith("cdn.openvk.org/", StringComparison.OrdinalIgnoreCase))
        return "https://" + hostAndPath;

      return url;
    }

    private static int GetPositiveInt(JToken value)
    {
      if (value == null)
        return 0;

      int result;
      return int.TryParse(value.ToString(), out result) && result > 0 ? result : 0;
    }

    private static long GetLong(JToken value)
    {
      if (value == null)
        return 0L;

      long result;
      return long.TryParse(value.ToString(), out result) ? result : 0L;
    }

    private static string GetString(JToken value)
    {
      if (value == null || value.Type == JTokenType.Null)
        return "";
      return value.Type == JTokenType.String ? value.Value<string>() : value.ToString();
    }

    private static bool IsEmptyString(JToken value)
    {
      return string.IsNullOrWhiteSpace(GetString(value));
    }

    private static bool IsNullOrMissing(JToken value)
    {
      return value == null ||
             value.Type == JTokenType.Null ||
             value.Type == JTokenType.Undefined;
    }

    private static bool IsHttpUrl(string value)
    {
      return !string.IsNullOrWhiteSpace(value) &&
             (value.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
              value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              value.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLegacyIntegerBoolean(string propertyName)
    {
      string name = (propertyName ?? "").ToLowerInvariant();
      return name == "online" ||
             name == "online_mobile" ||
             name == "user_likes" ||
             name == "verified" ||
             name == "out" ||
             name == "read_state" ||
             name == "important" ||
             name == "deleted" ||
             name == "emoji" ||
             name == "friends_only" ||
             name == "marked_as_ads" ||
             name == "fixed_post" ||
             name.StartsWith("is_", StringComparison.Ordinal) ||
             name.StartsWith("can_", StringComparison.Ordinal);
    }

    private sealed class ImageSize
    {
      public string Url { get; private set; }
      public int Width { get; private set; }
      public int Height { get; private set; }

      public ImageSize(string url, int width, int height)
      {
        this.Url = url;
        this.Width = width;
        this.Height = height;
      }
    }
  }
}
