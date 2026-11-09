using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VKClient.Common.Framework;

namespace VKClient.Common.Backend.DataObjects
{
  public class Poll : IBinarySerializable
  {
    private string _question = "";
    private List<long> _answerIds = new List<long>();
    private long _answerId;

    public long owner_id { get; set; }

    public long poll_id
    {
      get
      {
        return this.id;
      }
      set
      {
        this.id = value;
      }
    }

    public long id { get; set; }

    public int created { get; set; }

    public int is_closed { get; set; }

    public string question
    {
      get
      {
        return this._question;
      }
      set
      {
        this._question = (value ?? "").ForUI();
      }
    }

    public int votes { get; set; }

    public long answer_id
    {
      get { return this._answerId; }
      set
      {
        this._answerId = value;
        if (value <= 0) this._answerIds = new List<long>();
        else if (this._answerIds.Count == 0 || this._answerIds[0] != value) this._answerIds = new List<long> { value };
      }
    }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<long> answer_ids
    {
      get
      {
        return this._answerIds;
      }
      set
      {
        this._answerIds = (value ?? new List<long>()).Where(id => id > 0).Distinct().ToList();
        this._answerId = this._answerIds.Count > 0 ? this._answerIds[0] : 0L;
      }
    }

    public List<Answer> answers { get; set; }

    public int anonymous { get; set; }

    public Poll()
    {
      this.answers = new List<Answer>();
    }

    public void Write(BinaryWriter writer)
    {
      writer.Write(4);
      writer.Write(this.owner_id);
      writer.Write(this.poll_id);
      writer.Write(this.created);
      writer.Write(this.is_closed);
      writer.WriteString(this.question);
      writer.Write(this.votes);
      writer.Write(this.answer_id);
      writer.WriteList<Answer>((IList<Answer>) this.answers, 10000);
      writer.Write(this.anonymous);
      writer.WriteList(this.answer_ids);
    }

    public void Read(BinaryReader reader)
    {
      int num1 = reader.ReadInt32();
      this.owner_id = reader.ReadInt64();
      this.poll_id = reader.ReadInt64();
      int num2 = 2;
      if (num1 >= num2)
      {
        this.created = reader.ReadInt32();
        this.is_closed = reader.ReadInt32();
        this.question = reader.ReadString();
        this.votes = reader.ReadInt32();
        this.answer_id = reader.ReadInt64();
        this.answers = reader.ReadList<Answer>();
      }
      int num3 = 3;
      if (num1 < num3)
        return;
      this.anonymous = reader.ReadInt32();
      if (num1 >= 4) this.answer_ids = reader.ReadListLong();
    }
  }
}
