using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class GetInfoPeers
    {
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("node_id")] public string NodeId { get; set; }
        [JsonProperty("channel_id")] public string ChannelId { get; set; }
        [JsonProperty("short_channel_id")] public string ShortChannelId { get; set; }
        [JsonProperty("funding_tx")] public string FundingTx { get; set; }
        [JsonProperty("funding_vout")] public int FundingVout { get; set; }
        [JsonProperty("confirmation")] public int Confirmation { get; set; }
        [JsonProperty("feerate_per_kw")] public string FeeratePerKw { get; set; }
        [JsonProperty("local")] public GetInfoPeerValue Local { get; set; }
        [JsonProperty("remote")] public GetInfoPeerValue Remote { get; set; }

    }
}