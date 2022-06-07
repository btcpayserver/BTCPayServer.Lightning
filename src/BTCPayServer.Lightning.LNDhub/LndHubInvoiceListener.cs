using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNDhub.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.LndHub
{
    public class LndHubInvoiceListener : ILightningInvoiceListener
    {
        private readonly LndHubClient _client;
        private readonly Channel<LightningInvoice> _invoices = Channel.CreateBounded<LightningInvoice>(50);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private HttpClient _httpClient;
        private HttpResponseMessage _response;
        private Stream _body;
        private StreamReader _reader;
        private Task _listenLoop;
        private readonly List<string> _paidInvoiceIds;

        public LndHubInvoiceListener(LndHubClient lndHubClient)
        {
            _client = lndHubClient;
            _paidInvoiceIds = new List<string>();
        }

        public Task StartListening(string streamUrl, string accessToken, CancellationToken cancellation = default)
        {
            try
            {
                _listenLoop = ListenLoop();
                
                // FIXME: This websocket based version would work with LNDhub.go, see:
                // https://ln.getalby.com/swagger/index.html#/Invoice/get_invoices_stream
                /*
                _httpClient = new HttpClient();
                _httpClient.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
                
                var req = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                req.Headers.Add("User-Agent", "BTCPayServer.Lightning.LndHubClient");

                _listenLoop = ListenLoop(req, cancellation);
                */
            }
            catch
            {
                Dispose();
            }
            
            return Task.CompletedTask;
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

        private async Task ListenLoop()
        {
            try
            {
                retry:
                while (!_cts.IsCancellationRequested)
                {
                    var invoicesData = await _client.GetInvoices(_cts.Token);
                    foreach (var data in invoicesData)
                    {
                        var invoice = LndHubUtil.ToLightningInvoice(data);
                        if (invoice.PaidAt != null && !_paidInvoiceIds.Contains(invoice.Id))
                        {
                            await _invoices.Writer.WriteAsync(invoice, _cts.Token);
                            _paidInvoiceIds.Add(invoice.Id);
                        }
                    }

                    await Task.Delay(2500, _cts.Token);
                    goto retry;
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
                Dispose(false);
            }
        }

        // FIXME: This websocket based version would work with LNDhub.go, see:
        // https://ln.getalby.com/swagger/index.html#/Invoice/get_invoices_stream
        /*
        private async Task ListenLoop(HttpRequestMessage request, CancellationToken cancellation = default)
        {
            try
            {
                _response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
                _body = await _response.Content.ReadAsStreamAsync();
                _reader = new StreamReader(_body);
                while(!cancellation.IsCancellationRequested)
                {
                    var line = await WithCancellation(_reader.ReadLineAsync(), cancellation);
                    if (line == null) continue;
                    
                    if (line.StartsWith("{\"result\":", StringComparison.OrdinalIgnoreCase))
                    {
                        var invoiceString = JObject.Parse(line)["invoice"].ToString();
                        var data = JsonConvert.DeserializeObject<InvoiceData>(invoiceString);
                        var invoice = LndHubUtil.ToLightningInvoice(data);
                        await _invoices.Writer.WriteAsync(invoice, cancellation);
                    }
                    else if (line.StartsWith("{\"error\":", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new LndHubClient.LndHubApiException(line);
                    }
                    else
                    {
                        throw new LndHubClient.LndHubApiException("Unknown result from LNDHub: " + line);
                    }
                }
            }
            catch when(cancellation.IsCancellationRequested)
            {
            }
            catch(Exception ex)
            {
                _invoices.Writer.TryComplete(ex);
            }
            finally
            {
                Dispose(false);
            }
        }

        private static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var waiting = Task.Delay(-1, delayCts.Token);
            await Task.WhenAny(waiting, task);
            delayCts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return await task;
        }
        */

        private void Dispose(bool waitLoop)
        {
            if(_cts.IsCancellationRequested)
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
