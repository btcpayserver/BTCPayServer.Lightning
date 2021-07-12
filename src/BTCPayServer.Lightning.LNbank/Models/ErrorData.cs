using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class ErrorData
    {
        public int Status { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }

        [JsonProperty("errorCode")]
        public string Code { get; set; }
    }
}
