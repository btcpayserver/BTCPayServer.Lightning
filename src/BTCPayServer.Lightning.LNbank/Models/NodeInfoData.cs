using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class NodeInfoData
    {
        public int BlockHeight { get; set; }

        [JsonProperty("nodeURIs")]
        public List<string> NodeURIs { get; set; }
    }
}
