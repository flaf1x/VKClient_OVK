using System;
using System.IO;
using VKClient.Audio.Base;
using VKClient.Audio.Base.BackendServices;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library;

namespace VKClient.Common.Graffiti
{
  public class GraffitiAttachmentItem : IBinarySerializable
  {
    private string _uri;
    private int _width;
    private int _height;
    private bool _isUploading;
    private Cancellation _cancellation;
    private Photo _photo;

    public Doc Doc { get; private set; }

    public bool IsUploaded { get; private set; }

    public string AttachmentId
    {
      get
      {
        if (this._photo != null)
          return Photo.ComposeAttachmentId(this._photo.owner_id, this._photo.pid, this._photo.access_key);
        if (this.Doc == null)
          return "";
        long ownerId = this.Doc.owner_id;
        long id = this.Doc.id;
        string accessKey = this.Doc.access_key;
        string str = string.Format("doc{0}_{1}", ownerId, id);
        if (ownerId != AppGlobalStateManager.Current.LoggedInUserId && !string.IsNullOrEmpty(accessKey))
          str += string.Format("_{0}", accessKey);
        return str;
      }
    }

    public GraffitiAttachmentItem()
    {
    }

    public GraffitiAttachmentItem(string uri, int width, int height)
    {
      this._uri = uri;
      this._width = width;
      this._height = height;
      Doc doc = new Doc();
      DocPreview docPreview = new DocPreview();
      DocPreviewGraffiti docPreviewGraffiti = new DocPreviewGraffiti();
      docPreviewGraffiti.width = width;
      docPreviewGraffiti.height = height;
      string uri1 = this._uri;
      docPreviewGraffiti.src = uri1;
      docPreview.graffiti = docPreviewGraffiti;
      doc.preview = docPreview;
      this.Doc = doc;
    }

    public void Upload(Action completionCallback, Action<double> progressCallback = null)
    {
      if (this._isUploading)
        return;
      if (this._uri == null)
      {
        Action action = completionCallback;
        if (action == null)
          return;
        action();
      }
      else
      {
        if (this.IsUploaded)
        {
          completionCallback();
          return;
        }
        this._isUploading = true;
        Stream stream = ImageCache.Current.GetCachedImageStream(this._uri);
        if (stream == null)
        {
          this._isUploading = false;
          completionCallback();
          return;
        }
        byte[] image;
        using (stream)
        using (MemoryStream buffer = new MemoryStream())
        {
          stream.CopyTo(buffer);
          image = buffer.ToArray();
        }
        // OpenVK has no docs upload API. Send the drawing through the supported
        // message-photo upload endpoint, retaining the original image.
        MessagesService.Instance.UploadPhoto(image, (Action<BackendResult<Photo, ResultCode>>) (response =>
        {
          this.IsUploaded = response.ResultCode == ResultCode.Succeeded;
          if (this.IsUploaded)
          {
            this._photo = response.ResultData;
            ImageCache.Current.TryRemoveUri(this._uri);
          }
          this._isUploading = false;
          completionCallback();
        }), (Action<double>) (progress =>
        {
          Action<double> action = progressCallback;
          if (action == null)
            return;
          double num = progress;
          action(num);
        }));
      }
    }

    public Attachment CreateAttachment()
    {
      if (this._photo != null)
        return new Attachment() { type = "photo", photo = this._photo };
      Attachment attachment = new Attachment();
      attachment.type = "doc";
      Doc doc = this.Doc;
      attachment.doc = doc;
      return attachment;
    }

    public void Write(BinaryWriter writer)
    {
      writer.Write(2);
      writer.WriteString(this._uri);
      writer.Write(this._width);
      writer.Write(this._height);
      writer.Write<Doc>(this.Doc, false);
      writer.Write<Photo>(this._photo, false);
    }

    public void Read(BinaryReader reader)
    {
      int version = reader.ReadInt32();
      this._uri = reader.ReadString();
      this._width = reader.ReadInt32();
      this._height = reader.ReadInt32();
      this.Doc = reader.ReadGeneric<Doc>();
      if (version >= 2)
      {
        this._photo = reader.ReadGeneric<Photo>();
        this.IsUploaded = this._photo != null;
      }
    }
  }
}
