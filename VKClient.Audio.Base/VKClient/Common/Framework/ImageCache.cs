using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using VKClient.Common.Utils;

namespace VKClient.Common.Framework
{
  public class ImageCache : IBinarySerializable
  {
    public static string COMMUNITY_IMAGE = "https://vk.com/images/community";
    private Dictionary<string, string> _uriToLocalPathDict = new Dictionary<string, string>();
    private object _lockObj = new object();
    private readonly SHA1 _hasher = (SHA1) new SHA1Managed();
    private static ImageCache _current;

    public static ImageCache Current
    {
      get
      {
        if (ImageCache._current == null)
          ImageCache._current = new ImageCache();
        return ImageCache._current;
      }
    }

    public bool HasImageInCache(string uri)
    {
      return this._uriToLocalPathDict.ContainsKey(uri);
    }

    public Stream GetCachedImageStream(string uriString)
    {
      if (uriString == null)
        return  null;
      string empty = string.Empty;
      // Old VK used a few well-known *.gif URLs as semantic placeholders.
      // Do not classify every GIF on the internet as one of those placeholders:
      // OpenVK legitimately uses GIF for many resized images.
      if (uriString.StartsWith(ImageCache.COMMUNITY_IMAGE, StringComparison.OrdinalIgnoreCase))
        return Application.GetResourceStream(new Uri("/VKClient.Common;component/Resources/community_100.png", UriKind.RelativeOrAbsolute)).Stream;
      if (uriString.StartsWith("https://vk.com/images/deactivated", StringComparison.OrdinalIgnoreCase))
        return Application.GetResourceStream(new Uri("/VKClient.Common;component/Resources/deactivatedUser.png", UriKind.RelativeOrAbsolute)).Stream;
      if (uriString.StartsWith("https://vk.com/images/contact", StringComparison.OrdinalIgnoreCase))
        return Application.GetResourceStream(new Uri("/VKClient.Common;component/Resources/EmailUser.png", UriKind.RelativeOrAbsolute)).Stream;
      if (uriString.IndexOf("_null", StringComparison.OrdinalIgnoreCase) >= 0)
        return Application.GetResourceStream(new Uri("/VKClient.Common;component/Resources/Empty1x1.png", UriKind.RelativeOrAbsolute)).Stream;
      lock (this._lockObj)
      {
        if (this._uriToLocalPathDict.ContainsKey(uriString))
          empty = this._uriToLocalPathDict[uriString];
      }
      if (!string.IsNullOrEmpty(empty))
      {
        IsolatedStorageFile storeForApplication = IsolatedStorageFile.GetUserStoreForApplication();
        if (storeForApplication.FileExists(empty))
          return (Stream) storeForApplication.OpenFile(empty, FileMode.Open, FileAccess.Read);
      }
      return  null;
    }

    public void TryRemoveUri(string uri)
    {
      if (string.IsNullOrWhiteSpace(uri))
        return;
      try
      {
        string localPathBy = this.GetLocalPathBy(uri);
        IsolatedStorageFile.GetUserStoreForApplication().DeleteFile(localPathBy);
        lock (this._lockObj)
          this._uriToLocalPathDict.Remove(localPathBy);
      }
      catch (Exception ex)
      {
        Logger.Instance.Error("TryRemoveUri failed", ex);
      }
    }

    public void Clear()
    {
      lock (this._lockObj)
      {
        using (IsolatedStorageFile store = IsolatedStorageFile.GetUserStoreForApplication())
        {
          foreach (string path in this._uriToLocalPathDict.Values)
          {
            try
            {
              if (!string.IsNullOrWhiteSpace(path) && store.FileExists(path))
                store.DeleteFile(path);
            }
            catch (Exception ex)
            {
              Logger.Instance.Error("ImageCache.Clear failed for " + path, ex);
            }
          }
        }
        this._uriToLocalPathDict.Clear();
      }
    }

    public bool TrySetImageForUri(string uriString, Stream responseStream)
    {
      if (responseStream != null)
      {
        if (!string.IsNullOrWhiteSpace(uriString))
        {
          try
          {
            string localPathBy = this.GetLocalPathBy(uriString);
            using (IsolatedStorageFileStream storageFileStream = IsolatedStorageFile.GetUserStoreForApplication().OpenFile(localPathBy, FileMode.Create, FileAccess.Write))
              responseStream.CopyTo((Stream) storageFileStream);
            lock (this._lockObj)
              this._uriToLocalPathDict[uriString] = localPathBy;
            return true;
          }
          catch (Exception ex)
          {
            Logger.Instance.Error("TrySetImageForUri", ex);
          }
          return false;
        }
      }
      return false;
    }

    private string GetLocalPathBy(string uriString)
    {
      byte[] bytes = Encoding.UTF8.GetBytes(uriString);
      byte[] hash;
      lock (this._hasher)
        hash = this._hasher.ComputeHash(bytes);
      return BitConverter.ToString(hash).Replace("-", "");
    }

    public void Write(BinaryWriter writer)
    {
      writer.WriteDictionary(this._uriToLocalPathDict);
    }

    public void Read(BinaryReader reader)
    {
      this._uriToLocalPathDict = reader.ReadDictionary();
    }
  }
}
