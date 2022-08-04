using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class NodeInfoData
    {
        [JsonProperty(PropertyName = "uris")]
        public IEnumerable<string> Uris { get; set; }
    
        [JsonProperty(PropertyName = "identity_pubkey")]
        public string IdentityPubkey { get; set; }
    
        [JsonProperty(PropertyName = "block_height")]
        public int BlockHeight { get; set; }
    }
}
