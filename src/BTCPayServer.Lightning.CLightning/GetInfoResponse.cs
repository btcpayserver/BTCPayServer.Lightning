using Newtonsoft.Json;

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
    [JsonProperty("num_peers")]
    public int NumPeers { get; set; }
    [JsonProperty("num_pending_channels")]
    public int NumPendingChannels { get; set; }
    [JsonProperty("num_active_channels")]
    public int NumActiveChannels { get; set; }
    [JsonProperty("num_inactive_channels")]
    public int NumInactiveChannels { get; set; }

    public class GetInfoAddress
    {
        public string Type { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
    }
}
