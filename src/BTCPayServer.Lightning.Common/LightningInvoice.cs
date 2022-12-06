using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning;

public class LightningInvoice
{
    public string Id { get; set; }
    public LightningInvoiceStatus Status { get; set; }
    public string BOLT11 { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public LightMoney Amount { get; set; }
    public LightMoney AmountReceived { get; set; }
    public Dictionary<ulong, string> CustomRecords { get; set; }
}
