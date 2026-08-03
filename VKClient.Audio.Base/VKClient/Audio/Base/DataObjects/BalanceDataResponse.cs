namespace VKClient.Audio.Base.DataObjects
{
  public class BalanceDataResponse
  {
    public VKList<StockItem> stockItems { get; set; }

    public double balance { get; set; }

    public double votes { get; set; }
  }
}
