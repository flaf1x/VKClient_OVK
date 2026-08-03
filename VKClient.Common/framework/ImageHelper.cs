using Microsoft.Xna.Framework.Media;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using VKClient.Audio.Base;
using VKClient.Common.Backend;
using VKClient.Common.Localization;
using VKClient.Common.UC;
using VKClient.Common.Utils;

namespace VKClient.Common.Framework
{
  public class ImageHelper
  {
    public static void SaveImage(BitmapImage image)
    {
      byte[] byteArray = ImageUtil.TryImageToByteArray((BitmapSource) image);
      if (byteArray == null || byteArray.Length == 0)
        return;
      string str = "VK_Saved_Photo_ " + DateTime.Now.Ticks.ToString();
      using (MediaLibrary mediaLibrary = new MediaLibrary())
        mediaLibrary.SavePicture(str, byteArray);
      new GenericInfoUC().ShowAndHideLater(CommonResources.PhotoIsSaved,  null);
    }

    public static void SaveImage(string originalUri, Action<bool> completed = null)
    {
      if (string.IsNullOrWhiteSpace(originalUri))
      {
        if (completed != null)
          completed(false);
        return;
      }
      MemoryStream destination = new MemoryStream();
      JsonWebRequest.Download(originalUri, destination, succeeded =>
      {
        byte[] bytes = succeeded ? destination.ToArray() : null;
        destination.Dispose();
        Execute.ExecuteOnUIThread(() =>
        {
          bool saved = false;
          try
          {
            if (bytes != null && bytes.Length > 0)
            {
              string name = "OpenVK_Photo_" + DateTime.Now.Ticks + ".jpg";
              using (MediaLibrary mediaLibrary = new MediaLibrary())
                mediaLibrary.SavePicture(name, bytes);
              new GenericInfoUC().ShowAndHideLater(CommonResources.PhotoIsSaved, null);
              saved = true;
            }
          }
          catch
          {
          }
          if (completed != null)
            completed(saved);
        });
      }, null, new Cancellation());
    }
  }
}
