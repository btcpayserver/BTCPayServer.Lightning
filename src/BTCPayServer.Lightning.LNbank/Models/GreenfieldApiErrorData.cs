using System;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class GreenfieldApiErrorData
    {
        public GreenfieldApiErrorData()
        {
        }
            
        public GreenfieldApiErrorData(string code, string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }
        
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }
    }
}
