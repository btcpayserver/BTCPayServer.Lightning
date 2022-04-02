using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNDhub.Models;
using NBitcoin;

namespace BTCPayServer.Lightning.LndHub
{
    public class LndHubLightningClient : ILightningClient
    {
        private readonly LndHubClient _client;
        private readonly Uri _baseUri;

        public LndHubLightningClient(Uri baseUri, string loginToken, Network network, HttpClient httpClient = null)
        {
            var parts = loginToken.Split(':');
            _baseUri = baseUri;
            _client = new LndHubClient(baseUri, parts[0], parts[1], network, httpClient);
        }

        public async Task<CreateAccountResponse> CreateAccount(CancellationToken cancellation = default)
        {
            return await _client.CreateAccount(cancellation);
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
        {
            var data = await _client.GetInfo(cancellation);

            var nodeInfo = new LightningNodeInformation
            {
                BlockHeight = data.BlockHeight
            };
            foreach (var nodeUri in data.NodeURIs)
            {
                if (NodeInfo.TryParse(nodeUri, out var info))
                    nodeInfo.NodeInfoList.Add(info);
            }

            return nodeInfo;
        }
        public Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<BitcoinAddress> GetDepositAddress()
        {
            throw new NotImplementedException();
        }

        public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo)
        {
            throw new NotImplementedException();
        }

        public Task CancelInvoice(string invoiceId)
        {
            throw new NotImplementedException();
        }

        public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }
    }
}
