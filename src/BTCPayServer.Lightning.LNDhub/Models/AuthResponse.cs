using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class AuthResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
        
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }
    }
}
