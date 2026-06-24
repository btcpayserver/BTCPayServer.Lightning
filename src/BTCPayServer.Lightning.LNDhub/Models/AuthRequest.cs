using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class AuthRequest
    {
        [JsonProperty("login", NullValueHandling = NullValueHandling.Ignore)]
        public string Login { get; set; }
        
        [JsonProperty("password", NullValueHandling = NullValueHandling.Ignore)]
        public string Password { get; set; }
    
        [JsonProperty(PropertyName = "refresh_token", NullValueHandling = NullValueHandling.Ignore)]
        public string RefreshToken { get; set; }
    }
}
