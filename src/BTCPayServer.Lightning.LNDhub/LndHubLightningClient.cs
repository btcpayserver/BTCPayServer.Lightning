using System;
using System.Linq;
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
        private readonly Network _network;

        public LndHubLightningClient(Uri baseUri, string login, string password, Network network, HttpClient httpClient = null)
        {
            _network = network;
            _client = new LndHubClient(baseUri, login, password, network, httpClient);
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

        public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
        {
            var balance = await _client.GetBalance(cancellation);
            var offchain = new OffchainBalance
            {
                Local = balance.BtcBalance.AvailableBalance
            };
            return new LightningNodeBalance(null, offchain);
        }

        public async Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        {
            return await _client.GetDepositAddress(cancellation);
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            var invoices = await _client.GetInvoices(cancellation);
            var data = invoices.FirstOrDefault(i => i.Id == invoiceId);
            return data == null ? null : LndHubUtil.ToLightningInvoice(data);
        }

        public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
        {
            var payments = await _client.GetTransactions(cancellation);
            var data = payments.FirstOrDefault(i => i.PaymentHash == paymentHash);
            return data == null ? null : LndHubUtil.ToLightningPayment(data);
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default)
        {
            return await (this as ILightningClient).CreateInvoice(new CreateInvoiceParams(amount, description, expiry), cancellation);
        }

        public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation = default)
        {
            var invoice = await _client.CreateInvoice(req, cancellation);

            // the response to addinvoice is incomplete, fetch the invoice to return that data
            return await GetInvoice(invoice.Id, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        {
            return await Pay(bolt11, null, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            try
            {
                var pr = BOLT11PaymentRequest.Parse(bolt11, _network);
                var payAmount = payParams?.Amount ?? pr.MinimumAmount;
                var data = await _client.Pay(bolt11, payParams, cancellation);
                var totalAmount = data.Decoded?.AmountMsat ?? data.AmountMsat;
                var feeAmount = data.PaymentRoute?.FeeMsat ?? totalAmount - payAmount;
                var response = new PayResponse(PayResult.Ok, new PayDetails
                {
                    TotalAmount = totalAmount,
                    FeeAmount = feeAmount
                });

                return response;
            }
            catch (LndHubClient.LndHubApiException exception)
            {
                // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#general-error-response
                var result = exception.ErrorCode == 5 ? PayResult.CouldNotFindRoute : PayResult.Error;
                return new PayResponse(result, exception.Message);
            }
        }

        async Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
        {
            return await _client.CreateInvoiceSession(cancellation);
        }

        public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }
    }
}
