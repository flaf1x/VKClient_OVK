using System;
using System.IO;
using System.Windows;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;

namespace VKClient.Common.Library.Posts
{
  public class OutboundDocumentAttachment : OutboundAttachmentBase
  {
    private Doc _pickedDocument;
    private DocumentHeader _documentHeader;

    public DocumentHeader DocumentHeader
    {
      get
      {
        return this._documentHeader ?? (this._documentHeader = new DocumentHeader(this._pickedDocument, 0, false, 0L));
      }
    }

    public string IconSource
    {
      get
      {
        return "/Resources/NewPost/MiniAttachmentsDocument.png";
      }
    }

    public string Thumb
    {
      get
      {
        DocumentHeader documentHeader = this.DocumentHeader;
        if (documentHeader == null)
          return  null;
        Doc document = documentHeader.Document;
        if (document == null)
          return  null;
        return document.PreviewUri;
      }
    }

    public string Title
    {
      get
      {
        return this.DocumentHeader.Name;
      }
    }

    public string Subtitle
    {
      get
      {
        return this.DocumentHeader.GetSizeString();
      }
    }

    public override OutboundAttachmentUploadState UploadState
    {
      get
      {
        return OutboundAttachmentUploadState.Completed;
      }
      set
      {
      }
    }

    public Visibility IsUploadingVisibility
    {
      get
      {
        return Visibility.Collapsed;
      }
    }

    public override bool IsGeo
    {
      get
      {
        return false;
      }
    }

    public override string AttachmentId
    {
      get
      {
        if (this._pickedDocument == null || this._pickedDocument.id <= 0L)
          return "";
        // Use the data object's canonical form for both own and foreign docs.
        // It retains an access key whenever OpenVK supplies one for a GIF.
        return this._pickedDocument.UniqueIdForAttachment;
      }
    }

    public override bool IsUploadAttachment
    {
      get
      {
        return false;
      }
    }

    public OutboundDocumentAttachment(Doc pickedDocument)
    {
      this._pickedDocument = pickedDocument;
    }

    public OutboundDocumentAttachment()
    {
    }

    public override Attachment GetAttachment()
    {
      Attachment attachment = new Attachment();
      attachment.type = "doc";
      Doc pickedDocument = this._pickedDocument;
      attachment.doc = pickedDocument;
      return attachment;
    }

    public override void Upload(Action callback, Action<double> progressCallback = null)
    {
      callback();
    }

    public override void Write(BinaryWriter writer)
    {
      writer.Write(VKConstants.SerializationVersion);
      writer.Write<Doc>(this._pickedDocument, false);
    }

    public override void Read(BinaryReader reader)
    {
      reader.ReadInt32();
      this._pickedDocument = reader.ReadGeneric<Doc>();
    }

    public override void SetRetryFlag()
    {
    }

    public override void RemoveAndCancelUpload()
    {
    }
  }
}
