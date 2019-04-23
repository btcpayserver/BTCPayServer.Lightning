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
    public class ChargeSession : ILightningInvoiceListener
    {
        private ClientWebSocket socket;
        public ChargeSession(ClientWebSocket socket)
        {
            this.socket = socket;
            var buffer = new byte[WebsocketHelper.ORIGINAL_BUFFER_SIZE];
            _Buffer = new ArraySegment<byte>(buffer, 0, buffer.Length);
        }

        ArraySegment<byte> _Buffer;
        public async Task<ChargeInvoice> WaitInvoice(CancellationToken cancellation = default(CancellationToken))
        {
            var buffer = _Buffer;
            var array = _Buffer.Array;
            var originalSize = _Buffer.Array.Length;
            var newSize = _Buffer.Array.Length;
            while (true)
            {
                var message = await socket.ReceiveAsync(buffer, cancellation);
                if (message.MessageType == WebSocketMessageType.Close)
                {
                    await WebsocketHelper.CloseSocketAndThrow(buffer, socket, WebSocketCloseStatus.NormalClosure, "Close message received from the peer", cancellation);
                    break;
                }
                if (message.MessageType != WebSocketMessageType.Text)
                {
                    await WebsocketHelper.CloseSocketAndThrow(buffer, socket, WebSocketCloseStatus.InvalidMessageType, "Only Text is supported", cancellation);
                    break;
                }
                if (message.EndOfMessage)
                {
                    buffer = new ArraySegment<byte>(array, 0, buffer.Offset + message.Count);
                    try
                    {
                        var o = ParseMessage(buffer);
                        if (newSize != originalSize)
                        {
                            Array.Resize(ref array, originalSize);
                        }
                        return o;
                    }
                    catch (Exception ex)
                    {
                        await WebsocketHelper.CloseSocketAndThrow(buffer, socket, WebSocketCloseStatus.InvalidPayloadData, $"Invalid payload: {ex.Message}", cancellation);
                    }
                }
                else
                {
                    if (buffer.Count - message.Count <= 0)
                    {
                        newSize *= 2;
                        if (newSize > WebsocketHelper.MAX_BUFFER_SIZE)
                            await WebsocketHelper.CloseSocketAndThrow(buffer, socket, WebSocketCloseStatus.MessageTooBig, "Message is too big", cancellation);
                        Array.Resize(ref array, newSize);
                        buffer = new ArraySegment<byte>(array, buffer.Offset, newSize - buffer.Offset);
                    }
                    buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + message.Count, buffer.Count - message.Count);
                }
            }
            throw new InvalidOperationException("Should never happen");
        }

        
        private ChargeInvoice ParseMessage(ArraySegment<byte> buffer)
        {
            return JsonConvert.DeserializeObject<ChargeInvoice>(WebsocketHelper.GetStringFromBuffer(buffer), new JsonSerializerSettings());
        }

        

        public async void Dispose()
        {
            await WebsocketHelper.CloseSocket(socket);
        }

        public async Task DisposeAsync()
        {
            await WebsocketHelper.CloseSocket(socket);
        }

        

        async Task<LightningInvoice> ILightningInvoiceListener.WaitInvoice(CancellationToken token)
        {
            return ChargeClient.ToLightningInvoice(await WaitInvoice(token));
        }

        void IDisposable.Dispose()
        {
            Dispose();
        }
    }
}
