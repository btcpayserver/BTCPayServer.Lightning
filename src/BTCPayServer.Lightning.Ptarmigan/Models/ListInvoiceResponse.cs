using System.Collections.Generic;
using Newtonsoft.Json;
namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class ListInvoiceResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("error")] public ErrorResponse Error { get; set; }
        [JsonProperty("result")] public List<ListInvoiceResultResponse> Result { get; set; }
    }
}
