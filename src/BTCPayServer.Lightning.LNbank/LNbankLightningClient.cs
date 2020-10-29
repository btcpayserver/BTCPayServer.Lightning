using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankLightningClient : ILightningClient
    {
        private readonly Uri _address;
        private readonly string _apiToken;
        private readonly Network _network;
        private readonly LNbankClient _lnbankClient;

        public LNbankLightningClient(Uri address, string apiToken, Network network, HttpClient httpClient = null)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _network = network;
            _apiToken = apiToken;
            _lnbankClient = new LNbankClient(address, apiToken, network, httpClient);
        }

        public Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest,
            CancellationToken cancellation = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default(CancellationToken))
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

        public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default(CancellationToken))
        {
            throw new NotImplementedException();
        }
    }
}
