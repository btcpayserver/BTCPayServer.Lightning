using System;
using System.Net.Http.Headers;
using System.Text;
using BTCPayServer.Lightning.Eclair.Models;
using Newtonsoft.Json.Linq;
using PureWebSockets;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairWebsocketClient
    {
        private readonly string _address;
        private readonly string _password;
        private PureWebSocket _websocketConnection;
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

        public void Connect()
        {
            _websocketConnection = new PureWebSocket(_address, new PureWebSocketOptions()
            {
                Headers = new[]
                {
                    new Tuple<string, string>("Authorization",
                        new AuthenticationHeaderValue("Basic",
                            Convert.ToBase64String(Encoding.Default.GetBytes($":{_password}"))).ToString())
                },
                DebugMode = true,
                MyReconnectStrategy = new ReconnectStrategy(2000, 4000, 20)
            });
            _websocketConnection.OnMessage += WebsocketConnectionOnOnMessage;
            _websocketConnection.OnData += WebsocketConnectionOnOnMessage2;
            _websocketConnection.OnError += exception => { Console.WriteLine(exception.Message); };
            _websocketConnection.Connect();
        }

        private void WebsocketConnectionOnOnMessage2(byte[] data)
        {
            throw new NotImplementedException();
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

        public void Dispose()
        {
            _websocketConnection.Dispose();
        }
    }
}