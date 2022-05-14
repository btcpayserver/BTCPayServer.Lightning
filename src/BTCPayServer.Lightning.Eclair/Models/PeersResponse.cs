namespace BTCPayServer.Lightning.Eclair.Models
{
    public class PeersResponse
    {
        public string NodeId { get; set; }
        public string State { get; set; }
        public string Address { get; set; }
        public int Channels { get; set; }
    }
}
