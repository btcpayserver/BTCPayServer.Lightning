using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class CreateInvoiceRequest
    {
        // Amount in satoshis
        [JsonProperty("amt")]
        public long Amount { get; set; }

        [JsonProperty("memo")]
        public string Memo { get; set; }

        [JsonProperty("description_hash")]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 DescriptionHash { get; set; }

    }
}
