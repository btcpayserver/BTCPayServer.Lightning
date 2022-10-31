#nullable enable
using System;
using System.Collections.Generic;
using NBitcoin;

namespace BTCPayServer.Lightning;

public class PayInvoiceParams
{
    public double? MaxFeePercent { get; set; }
    public Money? MaxFeeFlat { get; set; }
    public LightMoney? Amount { get; set; }

    public PubKey? Destination { get; set; }

    public uint256? PaymentHash { get; set; }

    public Dictionary<ulong, string>? CustomRecords { get; set; }
    
    public TimeSpan? SendTimeout { get; set; }
    public static TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(30.0);
}
