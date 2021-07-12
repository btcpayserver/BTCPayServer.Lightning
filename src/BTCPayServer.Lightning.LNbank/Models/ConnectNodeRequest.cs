using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class ConnectNodeRequest
    {
        [JsonProperty("nodeURI")]
        public string NodeURI { get; set; }
    }
}
