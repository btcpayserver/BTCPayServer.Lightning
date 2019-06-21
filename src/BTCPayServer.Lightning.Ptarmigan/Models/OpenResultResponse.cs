using Newtonsoft.Json;
namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class OpenResultResponse
    {
        [JsonProperty("status")] public string Status { get; set; }
    }
}
