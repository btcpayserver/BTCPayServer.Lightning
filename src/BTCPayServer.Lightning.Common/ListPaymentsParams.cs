namespace BTCPayServer.Lightning;

public class ListPaymentsParams
{
    public bool? IncludePending { get; set; }
    public long? OffsetIndex { get; set; }
}
