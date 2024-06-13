using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AsyncKeyedLock;
using BTCPayServer.Lightning.LNDhub.Models;
using NBitcoin;

namespace BTCPayServer.Lightning.LndHub
{
    public class LndHubInvoiceListener : ILightningInvoiceListener
    {
        private readonly LndHubClient _client;
        private readonly Channel<LightningInvoice> _invoices = Channel.CreateUnbounded<LightningInvoice>();
        private readonly CancellationTokenSource _cts;
        private HttpClient _httpClient;
        private HttpResponseMessage _response;
        private Stream _body;
        private StreamReader _reader;
        private Task _listenLoop;
        private readonly List<string> _paidInvoiceIds;

        public LndHubInvoiceListener(LndHubClient lndHubClient, CancellationToken cancellation)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            _client = lndHubClient;
            _paidInvoiceIds = new List<string>();
            _listenLoop = ListenLoop();
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            try
            {
                return await _invoices.Reader.ReadAsync(cancellation);
            }
            catch (ChannelClosedException ex) when(ex.InnerException == null)
            {
                throw new OperationCanceledException();
            }
            catch (ChannelClosedException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        static readonly AsyncKeyedLocker<string> _locker = new(o =>
        {
            o.PoolSize = 20;
            o.PoolInitialFill = 1;
        });
        static readonly ConcurrentDictionary<string, InvoiceData[]> _activeListeners = new();
        
        private async Task ListenLoop()
        {
            try
            {
                AsyncKeyedLockTimeoutReleaser<string> releaser = null;
                try
                {
                    releaser = await _locker.LockAsync(_client.CacheKey, 0, _cts.Token);
                    if (!releaser.EnteredSemaphore)
                    {
                        while (!_cts.IsCancellationRequested && !releaser.EnteredSemaphore)
                        {
                            if (_activeListeners.TryGetValue(_client.CacheKey, out var invoicesData))
                            {
                                await HandleInvoicesData(invoicesData);
                            }
                            releaser = await _locker.LockAsync(_client.CacheKey, 0, _cts.Token);

                            if (!releaser.EnteredSemaphore)
                                await Task.Delay(2500, _cts.Token);
                        }
                    }

                    while (!_cts.IsCancellationRequested)
                    {
                        var invoicesData = await _client.GetInvoices(_cts.Token);
                        _activeListeners.AddOrReplace(_client.CacheKey, invoicesData);
                        await HandleInvoicesData(invoicesData);

                        await Task.Delay(2500, _cts.Token);
                    }
                }
                finally
                {
                    releaser.Dispose();
                }
            }
            catch when (_cts.IsCancellationRequested)
            {
            }
            catch(Exception ex)
            {
                _invoices.Writer.TryComplete(ex);
            }
            finally
            {
                _activeListeners.TryRemove(_client.CacheKey, out _);
                Dispose(false);
            }
        }

        private async Task HandleInvoicesData(IEnumerable<InvoiceData> invoicesData)
        {
            foreach (var data in invoicesData)
            {
                var invoice = LndHubUtil.ToLightningInvoice(data);
                if (invoice.PaidAt != null && !_paidInvoiceIds.Contains(invoice.Id))
                {
                    await _invoices.Writer.WriteAsync(invoice, _cts.Token);
                    _paidInvoiceIds.Add(invoice.Id);
                }
            }
        }

        private void Dispose(bool waitLoop)
        {
            if (_cts.IsCancellationRequested)
                return;
            _cts.Cancel();
            _reader?.Dispose();
            _reader = null;
            _body?.Dispose();
            _body = null;
            _response?.Dispose();
            _response = null;
            _httpClient?.Dispose();
            _httpClient = null;
            if (waitLoop)
                _listenLoop?.Wait();
            _invoices.Writer.TryComplete();
        }
    }
}
