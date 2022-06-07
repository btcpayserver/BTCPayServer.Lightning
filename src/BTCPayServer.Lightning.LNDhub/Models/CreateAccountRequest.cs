using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class CreateAccountRequest
    {
        [JsonProperty("accounttype")]
        public string AccountType { get; set; }
    }
}
