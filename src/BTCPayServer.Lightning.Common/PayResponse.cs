using System;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Lightning
{
    public enum PayResult
    {
        Ok,
        Unknown,
        CouldNotFindRoute,
        Error
    }

    public class PayResponse
    {
        // parameterless constructor for JSON serialization
        public PayResponse() {}

        public PayResponse(PayResult result)
        {
            Result = result;
        }

        public PayResponse(PayResult result, string errorDetail)
        {
            Result = result;
            ErrorDetail = errorDetail;
        }

        public PayResponse(PayResult result, PayDetails details)
        {
            Result = result;
            Details = details;
        }

        public PayResult Result { get; set; }
        public PayDetails Details { get; set; }
        public string ErrorDetail { get; set; }
    }

    public class PayDetails
    {
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney TotalAmount { get; set; }
        
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney FeeAmount { get; set; }
        
        [JsonConverter(typeof(StringEnumConverter))]
        public LightningPaymentStatus Status { get; set; }
        
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 Preimage { get; set; }
        
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 PaymentHash { get; set; }
    }
}
