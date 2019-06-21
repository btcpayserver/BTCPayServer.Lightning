using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class ListPaymentResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("result")] public List<ListPaymentResultResponse> Result { get; set; }
    }
}