namespace BTCPayServer.Lightning.CLightning;

//[{"type":"ipv4","address":"52.166.90.122","port":9735}]
public class GetInfoResponse
{
    public string Id { get; set; }
    public GetInfoAddress[] Address { get; set; }
    public string Version { get; set; }
    public string Color { get; set; }
    public string Alias { get; set; }
    public string Network { get; set; }
    public int BlockHeight { get; set; }
    public int NumPeers { get; set; }
    public int NumPendingChannels { get; set; }
    public int NumActiveChannels { get; set; }
    public int NumInactiveChannels { get; set; }

    public class GetInfoAddress
    {
        public string Type { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
    }
}
