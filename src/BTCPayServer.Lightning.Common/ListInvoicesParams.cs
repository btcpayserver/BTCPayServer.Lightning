namespace BTCPayServer.Lightning;

public class ListInvoicesParams
{
    public bool? PendingOnly { get; set; }
    public long? OffsetIndex { get; set; }
}
