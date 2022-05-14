namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class NetworkFeesResponse
    {
        public string RemoteNodeId { get; set; }
        public string ChannelId { get; set; }
        public string TxId { get; set; }
        public long FeeSat { get; set; }
        public string TxType { get; set; }
        public long Timestamp { get; set; }
    }
}
