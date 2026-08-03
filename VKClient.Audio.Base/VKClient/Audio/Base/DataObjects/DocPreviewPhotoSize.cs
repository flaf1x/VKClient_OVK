using System.IO;
using VKClient.Common.Framework;

namespace VKClient.Audio.Base.DataObjects
{
  public sealed class DocPreviewPhotoSize : IBinarySerializable
  {
    public string src { get; set; }

    // OpenVK uses the modern VK API field name "url" for document-preview
    // sizes, while this legacy client expects "src".  Keep the binary format
    // unchanged and normalize the JSON payload during deserialization.
    public string url
    {
      set
      {
        if (string.IsNullOrWhiteSpace(this.src))
          this.src = value;
      }
    }

    public int width { get; set; }

    public int height { get; set; }

    public string type { get; set; }

    public void Write(BinaryWriter writer)
    {
      writer.Write(2);
      writer.WriteString(this.src);
      writer.Write(this.width);
      writer.Write(this.height);
      writer.WriteString(this.type);
    }

    public void Read(BinaryReader reader)
    {
      int num1 = reader.ReadInt32();
      this.src = reader.ReadString();
      int num2 = 2;
      if (num1 < num2)
        return;
      this.width = reader.ReadInt32();
      this.height = reader.ReadInt32();
      this.type = reader.ReadString();
    }
  }
}
