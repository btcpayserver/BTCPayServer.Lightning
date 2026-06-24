using System;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#general-error-response
    public class ErrorResponse
    {
        [JsonProperty(PropertyName = "error")]
        public bool Error { get; } = true;
    
        [JsonProperty(PropertyName = "code")]
        public int Code { get; set; }

        [JsonProperty(PropertyName = "message")]
        public string Message
        {
            get => !string.IsNullOrEmpty(CustomMessage)
                ? CustomMessage
                : Code switch
                {
                    1 => "Bad auth",
                    2 => "Not enough balance",
                    3 => "Bad partner",
                    4 => "Not a valid invoice",
                    5 => "Route not found",
                    6 => "General server error",
                    7 => "Node failure",
                    _ => throw new ArgumentOutOfRangeException()
                };
        }
    
        private string CustomMessage { get; }

        public ErrorResponse(int code, string customMessage = null)
        {
            Code = code;
            CustomMessage = customMessage;
        }
        
        // parameterless constructor for JSON serialization
        public ErrorResponse() {}
    }

}
