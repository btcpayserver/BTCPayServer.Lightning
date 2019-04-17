using System;
using BTCPayServer.Lightning.Eclair.Models;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairWebsocketClient
    {
        private readonly string _address;
        private WebSocket _websocketConnection;
        public event EventHandler<object> PaymentEvent;
        public event EventHandler<PaymentRelayedEvent> PaymentRelayedEvent;
        public event EventHandler<PaymentReceivedEvent> PaymentReceivedEvent;
        public event EventHandler<PaymentFailedEvent> PaymentFailedEvent;
        public event EventHandler<PaymentSentEvent> PaymentSentEvent;
        public event EventHandler<PaymentSettlingOnChainEvent> PaymentSettlingOnChainEvent;
        public EclairWebsocketClient(string address)
        {
            _address = address;
        }

        public bool IsAlive => 
            _websocketConnection?.IsAlive ?? false;

        public void Connect()
        {
            if(_websocketConnection!= null)
            {
                Dispose();
            }
            _websocketConnection = new WebSocket(_address);
            _websocketConnection.OnMessage += WebsocketConnectionOnOnMessage;
            _websocketConnection.OnError += (sender, args) => { Console.WriteLine(args.Message); };
            _websocketConnection.Connect();
        }

        private void WebsocketConnectionOnOnMessage(object sender, MessageEventArgs e)
        {
            if (e.IsText)
            {
                var obj = JObject.Parse(e.Data);
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
        }

        public void Dispose()
        {
            _websocketConnection.Close();
        }
        
    }
}