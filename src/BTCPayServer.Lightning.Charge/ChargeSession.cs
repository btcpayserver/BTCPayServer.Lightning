using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Charge
{
    public class ChargeInvoice
    {
        public string Id { get; set; }

        [JsonProperty("msatoshi")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney MilliSatoshi { get; set; }
        [JsonProperty("msatoshi_received")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney MilliSatoshiReceived { get; set; }
        [JsonProperty("paid_at")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? PaidAt { get; set; }
        [JsonProperty("expires_at")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset ExpiresAt { get; set; }
        public string Status { get; set; }

        [JsonProperty("payreq")]
        public string PaymentRequest { get; set; }
        public string Label { get; set; }
    }
    public class ChargeSession : WebsocketListener, ILightningInvoiceListener
    {
        public ChargeSession(ClientWebSocket socket) : base(socket)
        {
        }

        public async Task<ChargeInvoice> WaitInvoice(CancellationToken cancellation = default)
        {
            var message = await WaitMessage(cancellation);
            return JsonConvert.DeserializeObject<ChargeInvoice>(message, new JsonSerializerSettings());
        }
        async Task<LightningInvoice> ILightningInvoiceListener.WaitInvoice(CancellationToken token)
        {
            return ChargeClient.ToLightningInvoice(await WaitInvoice(token));
        }
    }
}
