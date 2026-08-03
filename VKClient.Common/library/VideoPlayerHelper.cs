using Microsoft.Phone.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net;
using System.Threading;
using VKClient.Audio.Base.AudioCache;
using VKClient.Audio.Base.Events;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library.Media;
using VKClient.Common.Localization;
using VKClient.Common.Utils;

namespace VKClient.Common.Library
{
  public class VideoPlayerHelper
  {
      private const long MaxPrebufferedVideoSize = 25L * 1024L * 1024L;
      private const long Mp4ProbeEndByte = 65535L;

      public static string GetVideoUri(VKClient.Common.Backend.DataObjects.Video video, int resolution, out bool isExternal)
    {
      isExternal = false;
      if (video == null || video.files == null || video.files.Count == 0)
        return "";
      KeyValuePair<string, string> keyValuePair = (KeyValuePair<string, string>) Enumerable.Last<KeyValuePair<string, string>>(video.files);
      if (resolution > 0)
      {
        Dictionary<string, string>.Enumerator enumerator = video.files.GetEnumerator();
        try
        {
          while (enumerator.MoveNext())
          {
            KeyValuePair<string, string> current = enumerator.Current;
            if (((string) current.Key).Contains(resolution.ToString()))
            {
              keyValuePair = current;
              break;
            }
          }
        }
        finally
        {
          enumerator.Dispose();
        }
      }
      string str = keyValuePair.Value;
      isExternal = keyValuePair.Key == "external";
      if (((string) keyValuePair.Key).StartsWith("flv"))
        return "";
      return str;
    }

      public static bool CanPlayVideo(VKClient.Common.Backend.DataObjects.Video video)
    {
      bool isExternal;
      return VideoPlayerHelper.GetVideoUri(video, 0, out isExternal) != string.Empty;
    }

      public static void PlayVideo(VKClient.Common.Backend.DataObjects.Video video, Action callback, int resolution, StatisticsActionSource actionSource = StatisticsActionSource.news, string videoContext = "")
    {
      if ((video != null ? video.files :  null) == null || video.files.Count == 0)
      {
        callback.Invoke();
        ExtendedMessageBox.ShowSafe(video == null || video.processing != 0 ? CommonResources.ProcessingVideoError : CommonResources.Conversation_VideoIsNotAvailable);
      }
      else
      {
        bool isExternal;
        string uri = VideoPlayerHelper.GetVideoUri(video, resolution, out isExternal);
        if (string.IsNullOrEmpty(uri))
        {
          callback.Invoke();
          ExtendedMessageBox.ShowSafe(video.processing == 0 ? CommonResources.Conversation_VideoIsNotAvailable : CommonResources.ProcessingVideoError);
        }
        else
        {
          EventAggregator current = EventAggregator.Current;
          VideoPlayEvent videoPlayEvent = new VideoPlayEvent();
          videoPlayEvent.Position = StatisticsVideoPosition.start;
          int num1 = resolution;
          videoPlayEvent.quality = num1;
          string globallyUniqueId = video.GloballyUniqueId;
          videoPlayEvent.id = globallyUniqueId;
          int num2 = (int) actionSource;
          videoPlayEvent.Source = (StatisticsActionSource) num2;
          string str = videoContext;
          videoPlayEvent.Context = str;
          current.Publish(videoPlayEvent);
          ThreadPool.QueueUserWorkItem((WaitCallback) (o =>
          {
            Thread.Sleep(500);
            VideoPlayerHelper.PlayVideo(uri, isExternal, resolution, callback);
          }));
        }
      }
    }

      private static void PlayVideo(string uri, bool isExternal, int resolution, Action callback)
      {
          if (!isExternal)
          {
              VideoPlayerHelper.PlayNativeVideo(uri, callback);
              return;
          }

          Execute.ExecuteOnUIThread((Action)(() =>
          {
              string lowerInvariant = uri.ToLowerInvariant();
              if (lowerInvariant.Contains("youtube"))
                  VideoPlayerHelper.PlayYouTube(uri, resolution, callback);
              else if (lowerInvariant.Contains("instagram.com/"))
              {
                  callback();
                  VideoPlayerHelper.LaunchMediaPlayer(new Uri(uri, UriKind.Absolute));
              }
              else
              {
                  callback();
                  Navigator.Current.NavigateToWebUri(uri, false, false);
              }
          }));
      }

      private static void PlayNativeVideo(string uri, Action callback)
      {
          string cachedFile = MediaLRUCache.Instance.GetLocalFile(uri);
          if (VideoPlayerHelper.CachedFileExists(cachedFile))
          {
              VideoPlayerHelper.LaunchCachedVideo(cachedFile, callback);
              return;
          }

          string downloadUri = VideoPlayerHelper.RemoveFragment(uri);
          Uri parsedUri;
          if (!Uri.TryCreate(downloadUri, UriKind.Absolute, out parsedUri))
          {
              VideoPlayerHelper.LaunchRemoteVideo(downloadUri, callback);
              return;
          }

          // OpenVK keeps the MP4 index at the end of some uploaded videos.
          // Old Windows Phone players repeatedly seek through such streams,
          // which causes stalls. Probe the first 64 KiB and locally prebuffer
          // only small files that are not encoded for fast-start playback.
          JsonWebRequest.Download(downloadUri, 0L, Mp4ProbeEndByte, (status, totalSize, bytes) =>
          {
              bool shouldPrebuffer = status == HttpStatusCode.PartialContent &&
                                     totalSize > 0L && totalSize <= MaxPrebufferedVideoSize &&
                                     bytes != null && bytes.Length > 0 &&
                                     !VideoPlayerHelper.HasFastStartMoov(bytes);
              if (!shouldPrebuffer)
              {
                  VideoPlayerHelper.LaunchRemoteVideo(downloadUri, callback);
                  return;
              }
              VideoPlayerHelper.DownloadAndLaunchVideo(uri, downloadUri, callback);
          });
      }

      private static void DownloadAndLaunchVideo(string cacheKey, string downloadUri, Action callback)
      {
          string fileId = "video_" + Guid.NewGuid().ToString("N") + ".mp4";
          Stream destination = null;
          try
          {
              CacheManager.EnsureCacheFolderExists();
              destination = CacheManager.GetStreamForWrite(fileId);
              JsonWebRequest.Download(downloadUri, destination, succeeded =>
              {
                  int fileSize = 0;
                  try
                  {
                      fileSize = destination.Length > int.MaxValue ? 0 : (int)destination.Length;
                      destination.Dispose();
                  }
                  catch
                  {
                  }

                  if (succeeded && fileSize > 0)
                  {
                      MediaLRUCache.Instance.AddLocalFile(cacheKey, fileId, fileSize);
                      MediaLRUCache.Instance.Save();
                      VideoPlayerHelper.LaunchCachedVideo(fileId, callback);
                  }
                  else
                  {
                      CacheManager.TryDelete(fileId, CacheManager.DataType.CachedData);
                      VideoPlayerHelper.LaunchRemoteVideo(downloadUri, callback);
                  }
              }, null, new Cancellation());
          }
          catch
          {
              if (destination != null)
                  destination.Dispose();
              CacheManager.TryDelete(fileId, CacheManager.DataType.CachedData);
              VideoPlayerHelper.LaunchRemoteVideo(downloadUri, callback);
          }
      }

      private static bool CachedFileExists(string fileId)
      {
          if (string.IsNullOrEmpty(fileId))
              return false;
          try
          {
              using (IsolatedStorageFile store = IsolatedStorageFile.GetUserStoreForApplication())
                  return store.FileExists(CacheManager.GetFilePath(fileId, CacheManager.DataType.CachedData, "/"));
          }
          catch
          {
              return false;
          }
      }

      private static bool HasFastStartMoov(byte[] bytes)
      {
          int position = 0;
          while (position + 8 <= bytes.Length)
          {
              long size = ((long)bytes[position] << 24) |
                          ((long)bytes[position + 1] << 16) |
                          ((long)bytes[position + 2] << 8) |
                          bytes[position + 3];
              bool isMoov = bytes[position + 4] == (byte)'m' &&
                            bytes[position + 5] == (byte)'o' &&
                            bytes[position + 6] == (byte)'o' &&
                            bytes[position + 7] == (byte)'v';
              bool isMdat = bytes[position + 4] == (byte)'m' &&
                            bytes[position + 5] == (byte)'d' &&
                            bytes[position + 6] == (byte)'a' &&
                            bytes[position + 7] == (byte)'t';
              if (isMoov)
                  return true;
              if (isMdat)
                  return false;
              if (size < 8L || size > int.MaxValue || position + size > bytes.Length)
                  return true;
              position += (int)size;
          }
          return true;
      }

      private static string RemoveFragment(string uri)
      {
          int fragmentIndex = uri.IndexOf('#');
          return fragmentIndex >= 0 ? uri.Substring(0, fragmentIndex) : uri;
      }

      private static void LaunchCachedVideo(string fileId, Action callback)
      {
          Execute.ExecuteOnUIThread(() =>
          {
              callback();
              VideoPlayerHelper.LaunchMediaPlayer(new Uri(
                  CacheManager.GetFilePath(fileId, CacheManager.DataType.CachedData, "/"),
                  UriKind.Relative));
          });
      }

      private static void LaunchRemoteVideo(string uri, Action callback)
      {
          Execute.ExecuteOnUIThread(() =>
          {
              callback();
              VideoPlayerHelper.LaunchMediaPlayer(new Uri(uri, UriKind.Absolute));
          });
      }

      private static void PlayYouTube(string uri, int resolution, Action callback)
      {
          YouTubeExtractorAdapter.GetYouTubeVideoUri(uri, resolution, (Action<bool, Uri>)((success, resultUri) => Execute.ExecuteOnUIThread((Action)(() =>
          {
              if (!success)
              {
                  Navigator.Current.NavigateToWebUri(uri, false, false);
                  callback();
              }
              else
                  JsonWebRequest.GetHttpStatusCode(resultUri.ToString(), (Action<HttpStatusCode>)(statusCode =>
                  {
                      if (statusCode == HttpStatusCode.OK)
                          VideoPlayerHelper.LaunchMediaPlayer(resultUri);
                      else
                          Navigator.Current.NavigateToWebUri(uri, false, false);
                      callback();
                  }));
          }))));
      }

    private static void LaunchMediaPlayer(Uri uri)
    {
      Logger.Instance.Info(string.Concat("Launching media player for uri ", uri));
      MediaPlayerLauncher mediaPlayerLauncher = new MediaPlayerLauncher();
      Uri uri1 = uri;
      mediaPlayerLauncher.Media = uri1;
      int num1 = 31;
      mediaPlayerLauncher.Controls = ((MediaPlaybackControls) num1);
      int num2 = 2;
      mediaPlayerLauncher.Location = ((MediaLocationType) num2);
      mediaPlayerLauncher.Show();
    }
  }
}
