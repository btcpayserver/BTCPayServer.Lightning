using System;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class GreenfieldValidationErrorData
    {
        public GreenfieldValidationErrorData()
        {
        }
        
        public GreenfieldValidationErrorData(string path, string message)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        [JsonProperty("path")]
        public string Path { get; set; }
        
        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
