using System.IO;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;

namespace VKClient.Audio.Base.AudioCache
{
  public class RemoteFileInfo : IBinarySerializable
  {
    public string UriStr { get; set; }

    public long ownerId { get; set; }

    public long audioId { get; set; }

    public string Artist { get; set; }

    public string Title { get; set; }

    public string Duration { get; set; }

    public bool Added { get; set; }

    public string UniqueId
    {
      get
      {
        long num = this.ownerId;
        string str1 = num.ToString();
        string str2 = "_";
        num = this.audioId;
        string str3 = num.ToString();
        return str1 + str2 + str3;
      }
    }

    public RemoteFileInfo(AudioObj audioObj)
    {
      this.UriStr = audioObj.url;
      this.ownerId = audioObj.owner_id;
      this.audioId = audioObj.id;
      this.Artist = audioObj.artist;
      this.Title = audioObj.title;
      this.Duration = audioObj.duration;
      this.Added = audioObj.added;
    }

    public RemoteFileInfo()
    {
    }

    public void Write(BinaryWriter writer)
    {
      writer.Write(2);
      writer.WriteString(this.UriStr);
      writer.Write(this.ownerId);
      writer.Write(this.audioId);
      writer.WriteString(this.Artist);
      writer.WriteString(this.Title);
      writer.WriteString(this.Duration);
      writer.Write(this.Added);
    }

    public void Read(BinaryReader reader)
    {
      int version = reader.ReadInt32();
      this.UriStr = reader.ReadString();
      this.ownerId = reader.ReadInt64();
      this.audioId = reader.ReadInt64();
      if (version >= 2)
      {
        this.Artist = reader.ReadString();
        this.Title = reader.ReadString();
        this.Duration = reader.ReadString();
        this.Added = reader.ReadBoolean();
      }
    }

    public AudioObj ToAudioObj()
    {
      return new AudioObj()
      {
        owner_id = this.ownerId,
        id = this.audioId,
        artist = this.Artist,
        title = this.Title,
        duration = this.Duration,
        url = this.UriStr,
        added = this.Added
      };
    }
  }
}
