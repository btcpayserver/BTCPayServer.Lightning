using System;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class CreateInvoiceRequest
    {
        public string WalletId { get; set; }

        public string Description { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        public TimeSpan Expiry { get; set; }
    }
}
