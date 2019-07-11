using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Ptarmigan.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Lightning.Ptarmigan
{

    public class PtarmiganSession : WebsocketListener, ILightningInvoiceListener
    {

        private readonly PtarmiganLightningClient lightningClient;
        private readonly Network _network;
        private JsonSerializerSettings jsonSerializer;

        public PtarmiganSession(ClientWebSocket clientWebSocket, Network network, PtarmiganLightningClient lightningClient) : base(clientWebSocket)
        {
            this.lightningClient = lightningClient;
            _network = network;
            jsonSerializer = new JsonSerializerSettings
            { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
        retry:
            var message = await this.WaitMessage(cancellation);

            var listInvoiceResultResponse = JsonConvert.DeserializeObject<ListInvoiceResultResponse>(message, jsonSerializer);
            if (listInvoiceResultResponse != null)
            {
                return lightningClient.GetLightningInvoiceObject(listInvoiceResultResponse, _network);
            }

            goto retry;
        }
    }
}
