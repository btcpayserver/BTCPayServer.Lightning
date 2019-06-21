using Newtonsoft.Json;
namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class OpenResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("error")] public ErrorResponse Error { get; set; }
        [JsonProperty("result")] public OpenResultResponse Result { get; set; }
    }
}
