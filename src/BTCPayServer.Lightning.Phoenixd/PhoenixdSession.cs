using System;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Phoenixd.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.Phoenixd
{
    public class PhoenixdSession : WebsocketListener, ILightningInvoiceListener
    {
        private readonly PhoenixdLightningClient lightningClient;

        public PhoenixdSession(ClientWebSocket clientWebSocket, PhoenixdLightningClient lightningClient) : base(clientWebSocket)
        {
            this.lightningClient = lightningClient;
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
retry:
            var message = await this.WaitMessage(cancellation);
            var obj = JObject.Parse(message);
            object typedObj = null;
            switch (obj.GetValue("type").ToString())
            {
                case "payment_received":
                    typedObj = obj.ToObject<PaymentReceivedEvent>();
                    break;
            }

            if (typedObj is PaymentReceivedEvent r)
            {
                return await lightningClient.GetInvoice(r.PaymentHash);
            }
            goto retry;
        }
    }
}
