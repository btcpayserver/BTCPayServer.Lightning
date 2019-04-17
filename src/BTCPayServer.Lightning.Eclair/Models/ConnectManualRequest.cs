namespace BTCPayServer.Lightning.Eclair.Models
{
    public class ConnectManualRequest
    {
        public string NodeId { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
    }
}