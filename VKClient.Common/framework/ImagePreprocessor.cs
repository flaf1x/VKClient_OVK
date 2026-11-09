using ExifLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using VKClient.Common.ImageViewer;
using VKClient.Common.Library;
using VKClient.Common.Utils;

namespace VKClient.Common.Framework
{
  public static class ImagePreprocessor
  {
    public static Size GetChatBackgroundSize(int width, int height)
    {
      if (width <= 0 || height <= 0) throw new ArgumentException("Invalid photo dimensions");
      double scale = Math.Min(1, Math.Min((width > height ? 1280.0 : 720.0) / width,
        (width > height ? 720.0 : 1280.0) / height));
      return new Size(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static void PrepareChatBackground(Stream stream, Action<byte[]> completed)
    {
      // Read dimensions before decoding: an 8 MP camera image must not become
      // several full-size bitmaps on a 1 GB phone. The saved JPEG has no EXIF.
      try
      {
        stream.Position = 0;
        int width = 0, height = 0;
        var orientation = ExifOrientation.TopLeft;
        try
        {
          var info = new ExifReader(stream).info;
          width = info.Width; height = info.Height; orientation = info.Orientation;
        }
        catch (Exception) { }
        if (width <= 0 || height <= 0)
        {
          stream.Position = 0;
          byte[] header = new byte[24];
          int read = 0, count;
          while (read < header.Length && (count = stream.Read(header, read, header.Length - read)) > 0) read += count;
          if (read != 24 || header[0] != 137 || header[1] != 80 || header[2] != 78 || header[3] != 71 ||
              header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82)
            throw new ArgumentException("Unsupported photo format");
          width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
          height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        }
        Size size = GetChatBackgroundSize(width, height);
        var image = new BitmapImage();
        // Set one axis only so the decoder preserves the photograph's aspect.
        if (width >= height) image.DecodePixelWidth = (int)size.Width;
        else image.DecodePixelHeight = (int)size.Height;
        stream.Position = 0; image.SetSource(stream);
        var bitmap = new WriteableBitmap(image);
        if (orientation == ExifOrientation.BottomRight) bitmap = bitmap.Rotate(180);
        else if (orientation == ExifOrientation.TopRight) bitmap = bitmap.Rotate(90);
        else if (orientation == ExifOrientation.BottomLeft) bitmap = bitmap.Rotate(270);
        size = GetChatBackgroundSize(bitmap.PixelWidth, bitmap.PixelHeight);
        stream.Close();
        ThreadPool.QueueUserWorkItem(o => {
          byte[] bytes = null;
          try
          {
            using (var output = new MemoryStream())
            {
              System.Windows.Media.Imaging.Extensions.SaveJpeg(bitmap, output, (int)size.Width, (int)size.Height, 0, 85);
              bytes = output.ToArray();
            }
          }
          catch (Exception ex) { Logger.Instance.Error("Chat background encoding failed", ex); }
          completed(bytes);
        });
      }
      catch (Exception ex)
      {
        try { stream.Close(); } catch (Exception) { }
        Logger.Instance.Error("Chat background preparation failed", ex);
        completed(null);
      }
    }

    public static Rect GetChatAvatarCrop(int width, int height, Rect relative)
    {
      if (relative.Width <= 0 || relative.Height <= 0)
      {
        int side = Math.Min(width, height);
        return new Rect((width - side) / 2, (height - side) / 2, side, side);
      }
      int x = Math.Max(0, Math.Min(width - 1, (int)(relative.X * width)));
      int y = Math.Max(0, Math.Min(height - 1, (int)(relative.Y * height)));
      int w = Math.Max(1, Math.Min(width - x, (int)(relative.Width * width)));
      int h = Math.Max(1, Math.Min(height - y, (int)(relative.Height * height)));
      int size = Math.Min(w, h);
      return new Rect(x + (w - size) / 2, y + (h - size) / 2, size, size);
    }

    public static void PrepareChatAvatar(Stream stream, Rect crop, Action<byte[]> completed)
    {
      // The photo editor can return an already-read stream or a PNG. Do not
      // splice JPEG EXIF headers into it. Encode a bounded, cropped JPEG.
      try
      {
        stream.Position = 0;
        var image = new BitmapImage();
        image.SetSource(stream);
        var bitmap = new WriteableBitmap(image);
        try
        {
          stream.Position = 0;
          var exif = new ExifReader(stream);
          if (exif.info.Orientation == ExifOrientation.BottomRight) bitmap = bitmap.Rotate(180);
          else if (exif.info.Orientation == ExifOrientation.TopRight) bitmap = bitmap.Rotate(90);
          else if (exif.info.Orientation == ExifOrientation.BottomLeft) bitmap = bitmap.Rotate(270);
        }
        catch (Exception) { /* PNG and images without EXIF are valid. */ }
        var bounds = GetChatAvatarCrop(bitmap.PixelWidth, bitmap.PixelHeight, crop);
        bitmap = bitmap.Crop(bounds);
        int size = Math.Min(512, bitmap.PixelWidth);
        stream.Close();
        ThreadPool.QueueUserWorkItem(o => {
          byte[] bytes = null;
          try
          {
            using (var output = new MemoryStream())
            {
              System.Windows.Media.Imaging.Extensions.SaveJpeg(bitmap, output, size, size, 0, VKConstants.JPEGQUALITY);
              bytes = output.ToArray();
            }
          }
          catch (Exception ex) { Logger.Instance.Error("Chat avatar encoding failed", ex); }
          completed(bytes);
        });
      }
      catch (Exception ex)
      {
        try { stream.Close(); } catch (Exception) { }
        Logger.Instance.Error("Chat avatar preparation failed", ex);
        completed(null);
      }
    }

    public static Rect GetThumbnailRect(double actualWidth, double actualHeight, Rect relativeThumbRect)
    {
        Size childSize = new Size(actualWidth, actualHeight);
      return RectangleUtils.CalculateRelative(RectangleUtils.GetSize(RectangleUtils.ResizeToFitIfNotContained(new Size((double) VKConstants.MAX_PHOTO_WIDTH, (double) VKConstants.MAX_PHOTO_HEIGHT), childSize)), relativeThumbRect);
    }

    public static byte[] ReadFully(Stream input)
    {
      byte[] buffer = new byte[16384];
      using (MemoryStream memoryStream = new MemoryStream())
      {
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
          memoryStream.Write(buffer, 0, count);
        return memoryStream.ToArray();
      }
    }

    public static void PreprocessImage(Stream stream, int desiredSizeInPx, bool closeStream, Action<ImagePreprocessResult> completionCallback)
    {
      Stopwatch.StartNew();
      try
      {
        MemoryStream exifStream;
        ImagePreprocessor.PatchAwayExif(stream, out exifStream);
        long position = stream.Position;
        bool keepExif = AppGlobalStateManager.Current.GlobalState.SaveLocationDataOnUpload;
        stream.Position = 0L;
        BitmapImage bitmapImage = new BitmapImage();
        ((BitmapSource) bitmapImage).SetSource(stream);
        WriteableBitmap wb = new WriteableBitmap((BitmapSource) bitmapImage);
        ExifReader reader =  null;
        stream.Position = 0L;
        reader = new ExifReader(stream);
        bool flag = false;
        switch (reader.info.Orientation)
        {
          case ExifOrientation.BottomRight:
            wb = wb.Rotate(180);
            flag = true;
            break;
          case ExifOrientation.TopRight:
            wb = wb.Rotate(90);
            flag = true;
            break;
          case ExifOrientation.BottomLeft:
            wb = wb.Rotate(270);
            flag = true;
            break;
        }
        int pixelWidth = ((BitmapSource) wb).PixelWidth;
        int pixelHeight = ((BitmapSource) wb).PixelHeight;
        int num1 = pixelWidth * pixelHeight;
        if (desiredSizeInPx > num1)
          desiredSizeInPx = num1;
        if (num1 > desiredSizeInPx | flag)
        {
          MemoryStream ms = new MemoryStream();
          double num2 = Math.Sqrt((double) num1 / (double) desiredSizeInPx);
          int resizedWidth = (int) Math.Round((double) pixelWidth / num2);
          int resizedHeight = (int) Math.Round((double) pixelHeight / num2);
          MemoryStream resultStream;
          ThreadPool.QueueUserWorkItem((WaitCallback) (o =>
          {
            try
            {
                System.Windows.Media.Imaging.Extensions.SaveJpeg(wb, (Stream)ms, resizedWidth, resizedHeight, 0, VKConstants.JPEGQUALITY);
              ms.Position = 0L;
              if (keepExif)
              {
                exifStream = new MemoryStream(ImagePreprocessor.ResetOrientation(reader.info.OrientationOffset, exifStream.ToArray(), reader.info.LittleEndian));
                resultStream = ImagePreprocessor.MergeExif((Stream) ms, exifStream);
                Logger.Instance.Info("RESIZED JPEG SIZE: " + resultStream.Length);
                ms.Close();
              }
              else
                resultStream = ms;
              if (closeStream)
                stream.Close();
              exifStream.Close();
              resultStream.Position = 0L;
              GC.Collect();
              completionCallback(new ImagePreprocessResult((Stream) resultStream, resizedWidth, resizedHeight));
            }
            catch (Exception ex)
            {
              Logger.Instance.Error("Failed to resize image", ex);
            }
          }));
        }
        else
        {
          MemoryStream resultStream = new MemoryStream();
          if (keepExif)
          {
            stream.Position = 0L;
            stream.CopyTo((Stream) resultStream);
            if (closeStream)
              stream.Close();
          }
          else
          {
            stream.Position = 0L;
            resultStream.WriteByte((byte) stream.ReadByte());
            resultStream.WriteByte((byte) stream.ReadByte());
            stream.Position = position;
            stream.CopyTo((Stream) resultStream);
          }
          if (closeStream)
            stream.Close();
          exifStream.Close();
          resultStream.Position = 0L;
          GC.Collect();
          completionCallback(new ImagePreprocessResult((Stream) resultStream, pixelWidth, pixelHeight));
        }
      }
      catch (Exception ex)
      {
        Logger.Instance.Error("Failed to resize image", ex);
      }
    }

    public static byte[] ResetOrientation(long p, byte[] exifData, bool littleEndian)
    {
      byte[] array = new List<byte>((IEnumerable<byte>) exifData).ToArray();
      long num = p - 2L;
      if (num >= 0L && num < (long) (array.Length - 1))
        ExifIO.WriteUShort(array, (int) num, littleEndian, (ushort) 1);
      return array;
    }

    public static MemoryStream MergeExif(Stream ms, MemoryStream exifStream)
    {
      MemoryStream memoryStream = new MemoryStream();
      memoryStream.WriteByte((byte) ms.ReadByte());
      memoryStream.WriteByte((byte) ms.ReadByte());
      exifStream.WriteTo((Stream) memoryStream);
      ms.CopyTo((Stream) memoryStream);
      memoryStream.Position = 0L;
      return memoryStream;
    }

    public static void PatchAwayExif(Stream inStream, out MemoryStream exifStream)
    {
      exifStream = new MemoryStream();
      byte[] numArray = new byte[2]{ (byte) inStream.ReadByte(), (byte) inStream.ReadByte() };
      if ((int) numArray[0] != (int) byte.MaxValue || (int) numArray[1] != 216)
        return;
      ImagePreprocessor.SkipAppHeaderSection(inStream, out exifStream);
    }

    private static byte[] SkipAppHeaderSection(Stream inStream, out MemoryStream exifStream)
    {
      Stopwatch stopwatch = Stopwatch.StartNew();
      byte[] numArray = new byte[2]{ (byte) inStream.ReadByte(), (byte) inStream.ReadByte() };
      exifStream = new MemoryStream();
      while ((int) numArray[0] == (int) byte.MaxValue && (int) numArray[1] >= 224 && (int) numArray[1] <= 239)
      {
        exifStream.WriteByte(numArray[0]);
        exifStream.WriteByte(numArray[1]);
        byte num1 = (byte) inStream.ReadByte();
        byte num2 = (byte) inStream.ReadByte();
        exifStream.WriteByte(num1);
        exifStream.WriteByte(num2);
        byte[] buffer = new byte[((int) num1 << 8 | (int) num2) - 2];
        inStream.Read(buffer, 0, buffer.Length);
        exifStream.Write(buffer, 0, buffer.Length);
        numArray[0] = (byte) inStream.ReadByte();
        numArray[1] = (byte) inStream.ReadByte();
      }
      inStream.Position -= 2L;
      exifStream.Position = 0L;
      stopwatch.Stop();
      return numArray;
    }
  }
}
