using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class ConnectResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("error")] public ErrorResponse Error { get; set; }
        [JsonProperty("result")] public string Result { get; set; }
    }
}