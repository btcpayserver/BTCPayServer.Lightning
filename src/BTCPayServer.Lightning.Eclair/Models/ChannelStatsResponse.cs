namespace BTCPayServer.Lightning.Eclair.Models
{
   public partial class ChannelStatsResponse
        {
            public string ChannelId { get; set; }
            public long AvgPaymentAmountSatoshi { get; set; }
            public long PaymentCount { get; set; }
            public long RelayFeeSatoshi { get; set; }
            public long NetworkFeeSatoshi { get; set; }
        }
}