using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class GetInfoResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("result")] public GetInfoResultResponse Result { get; set; }
    }
}