using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class GetInfoResultResponse
    {
        [JsonProperty("node_id")] public string NodeId { get; set; }
        [JsonProperty("node_port")] public int NodePort { get; set; }
        [JsonProperty("announce_ip")] public string AnnounceIp { get; set; }
        [JsonProperty("total_local_msat")] public long TotalLocalMsat { get; set; }
        [JsonProperty("block_count")] public int? BlockCount { get; set; }
        [JsonProperty("peers")] public List<GetInfoPeers> Peers { get; set; }
    }
}