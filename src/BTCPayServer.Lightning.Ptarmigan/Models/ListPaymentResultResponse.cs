using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class ListPaymentResultResponse
    {
        [JsonProperty("payment_id")] public int PaymentId { get; set; }
        [JsonProperty("payment_hash")] public string PaymentHash { get; set; }
        [JsonProperty("additional_amount_msat")] public int AdditionalAmountMsat { get; set; }
        [JsonProperty("block_count")] public int BlockCount { get; set; }
        [JsonProperty("retry_count")] public int RetryCount { get; set; }
        [JsonProperty("max_retry_count")] public int MaxRetryCount { get; set; }
        [JsonProperty("auto_remove")] public bool AutoRemove { get; set; }
        [JsonProperty("state")] public string State { get; set; }

    }
}