using System;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Eclair.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairWebsocketClient
    {
        private readonly string _address;
        private readonly string _password;
        private ClientWebSocket _socket;
        private ArraySegment<byte> _buffer;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        public event EventHandler<object> PaymentEvent;
        public event EventHandler<PaymentRelayedEvent> PaymentRelayedEvent;
        public event EventHandler<PaymentReceivedEvent> PaymentReceivedEvent;
        public event EventHandler<PaymentFailedEvent> PaymentFailedEvent;
        public event EventHandler<PaymentSentEvent> PaymentSentEvent;
        public event EventHandler<PaymentSettlingOnChainEvent> PaymentSettlingOnChainEvent;

        public EclairWebsocketClient(string address, string password)
        {
            _address = address;
            _password = password;
        }

        public async Task Connect(CancellationToken cancellation = default(CancellationToken))
        {
            using (var cancellation2 =
                CancellationTokenSource.CreateLinkedTokenSource(cancellation, _cts.Token))
            {
                var rawBuffer = new byte[WebsocketHelper.ORIGINAL_BUFFER_SIZE];
                _buffer = new ArraySegment<byte>(rawBuffer, 0, rawBuffer.Length);
                _socket = await WebsocketHelper.CreateClientWebSocket(_address,
                    new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.Default.GetBytes($":{_password}"))).ToString(),
                    cancellation2.Token);


                var buffer = _buffer;
                var array = _buffer.Array;
                var originalSize = _buffer.Array.Length;
                var newSize = _buffer.Array.Length;
                while (!cancellation2.IsCancellationRequested)
                {
                    var message = await _socket.ReceiveAsync(buffer, cancellation2.Token);
                    if (message.MessageType == WebSocketMessageType.Close)
                    {
                        await WebsocketHelper.CloseSocketAndThrow(buffer, _socket,
                            WebSocketCloseStatus.NormalClosure,
                            "Close message received from the peer", cancellation2.Token);
                        break;
                    }

                    if (message.MessageType != WebSocketMessageType.Text)
                    {
                        await WebsocketHelper.CloseSocketAndThrow(buffer, _socket,
                            WebSocketCloseStatus.InvalidMessageType, "Only Text is supported", cancellation2.Token);
                        break;
                    }

                    if (message.EndOfMessage)
                    {
                        buffer = new ArraySegment<byte>(array, 0, buffer.Offset + message.Count);
                        try
                        {
                            var o = WebsocketHelper.GetStringFromBuffer(buffer);
                            if (newSize != originalSize)
                            {
                                Array.Resize(ref array, originalSize);
                            }

                            WebsocketConnectionOnOnMessage(o);
                        }
                        catch (Exception ex)
                        {
                            await WebsocketHelper.CloseSocketAndThrow(buffer, _socket,
                                WebSocketCloseStatus.InvalidPayloadData, $"Invalid payload: {ex.Message}",
                                cancellation2.Token);
                        }
                    }
                    else
                    {
                        if (buffer.Count - message.Count <= 0)
                        {
                            newSize *= 2;
                            if (newSize > WebsocketHelper.MAX_BUFFER_SIZE)
                                await WebsocketHelper.CloseSocketAndThrow(buffer, _socket,
                                    WebSocketCloseStatus.MessageTooBig, "Message is too big", cancellation);
                            Array.Resize(ref array, newSize);
                            buffer = new ArraySegment<byte>(array, buffer.Offset, newSize - buffer.Offset);
                        }

                        buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + message.Count,
                            buffer.Count - message.Count);
                    }
                }
            }
        }

        private void WebsocketConnectionOnOnMessage(string message)
        {
            var obj = JObject.Parse(message);
            object typedObj = null;
            switch (obj.GetValue("type").ToString())
            {
                case "payment-relayed":
                    typedObj = obj.ToObject<PaymentRelayedEvent>();
                    PaymentRelayedEvent?.Invoke(this, (PaymentRelayedEvent) typedObj);
                    break;
                case "payment-received":
                    typedObj = obj.ToObject<PaymentReceivedEvent>();
                    PaymentReceivedEvent?.Invoke(this, (PaymentReceivedEvent) typedObj);
                    break;
                case "payment-failed":
                    typedObj = obj.ToObject<PaymentFailedEvent>();
                    PaymentFailedEvent?.Invoke(this, (PaymentFailedEvent) typedObj);
                    break;
                case "payment-sent":
                    typedObj = obj.ToObject<PaymentSentEvent>();
                    PaymentSentEvent?.Invoke(this, (PaymentSentEvent) typedObj);
                    break;
                case "payment-settling-onchain":
                    typedObj = obj.ToObject<PaymentSettlingOnChainEvent>();
                    PaymentSettlingOnChainEvent?.Invoke(this, (PaymentSettlingOnChainEvent) typedObj);
                    break;
            }

            if (typedObj != null)
            {
                PaymentEvent?.Invoke(this, typedObj);
            }
        }

        public async void Dispose()
        {
            _cts.Cancel();
            await WebsocketHelper.CloseSocket(_socket);
        }
    }
}