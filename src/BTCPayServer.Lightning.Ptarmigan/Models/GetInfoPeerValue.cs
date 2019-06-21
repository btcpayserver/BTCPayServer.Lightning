using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class GetInfoPeerValue
    {
        [JsonProperty("msatoshi")] public long Msatoshi { get; set; }
        [JsonProperty("commit_num")] public int CommitNum { get; set; }
        [JsonProperty("num_htlc_outputs")] public int NumHtlcOutputs { get; set; }
    }
}

