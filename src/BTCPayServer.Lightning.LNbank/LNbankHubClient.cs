using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNbank.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankHubClient : ILightningInvoiceListener
    {
        private readonly LNbankLightningClient _lightningClient;
        private readonly HubConnection _connection;

        public LNbankHubClient(Uri baseUri, string apiToken, LNbankLightningClient lightningClient)
        {
            _lightningClient = lightningClient;
            _connection = new HubConnectionBuilder()
                .WithUrl($"{baseUri.AbsoluteUri}plugins/lnbank/hubs/transaction", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(apiToken);
                })
                .WithAutomaticReconnect()
                .Build();
        }

        Channel<LightningInvoice> _Invoices = Channel.CreateUnbounded<LightningInvoice>();
        public async Task Start(CancellationToken cancellation)
        {
            await _connection.StartAsync(cancellation);
            _connection.On<TransactionUpdateEvent>("transaction-update", async data =>
            {
                var id = data.PaymentHash ?? data.InvoiceId;
                var invoice = await _lightningClient.GetInvoice(id, cancellation);
                _Invoices.Writer.TryWrite(invoice);
            });
            _connection.Closed += (ex) =>
            {
                _Invoices.Writer.TryComplete(ex);
                return Task.CompletedTask;
            };
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            var canRead = await _Invoices.Reader.WaitToReadAsync(cancellation);
            if (!canRead || !_Invoices.Reader.TryRead(out var invoice))
                // All the channel completions should throw exception on the WaitToRead
                throw new InvalidOperationException("BUG 390902: This should never happen");
            return invoice;
        }

        public void Dispose()
        {
            _ = DisposeAsync();
        }

        private async Task DisposeAsync()
        {
            _Invoices.Writer.TryComplete(new ObjectDisposedException("The connection to LNBank got disposed"));
            try
            {
                await _connection.StopAsync();
            }
            catch
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch { }
            }
        }
    }
}
