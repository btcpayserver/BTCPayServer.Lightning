using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class AuthRequest
    {
        [JsonProperty("login")]
        public string Login { get; set; }
        [JsonProperty("password")]
        public string Password { get; set; }
    }
}
