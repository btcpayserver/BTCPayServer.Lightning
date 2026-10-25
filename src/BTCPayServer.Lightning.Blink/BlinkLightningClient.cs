using System;
using NBitcoin;

namespace BTCPayServer.Lightning.Blink
{
    public class BlinkLightningClient : ILightningClient
    {
        private BlinkApiClient _client;

        public BlinkLightningClient(Uri baseUri, string apiKey)
        {
            _client = new BlinkApiClient(baseUri, apiKey);
        }

        public Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default)
        {
            return _client.CreateLnInvoice(cancellation, amount.MilliSatoshi, expiry);
        }

        public Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = default)
        {
            return _client.CreateLnInvoice(cancellation, createInvoiceRequest.Amount, createInvoiceRequest.Expiry, createInvoiceRequest.Description);
        }

        public Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
        {
            return _client.GetBalance(cancellation);
        }

        public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default)
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
    }
}

