using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class NodeInfoData
    {
        [JsonProperty("block_height")]
        public int BlockHeight { get; set; }

        [JsonProperty("uris")]
        public List<string> NodeURIs { get; set; }
    }
}
