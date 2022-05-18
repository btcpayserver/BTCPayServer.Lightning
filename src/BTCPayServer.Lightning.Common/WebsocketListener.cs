using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Lightning
{
    public class WebsocketListener : IDisposable
    {
        private ClientWebSocket socket;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        public WebsocketListener(ClientWebSocket socket)
        {
            this.socket = socket;
            var buffer = new byte[WebsocketHelper.ORIGINAL_BUFFER_SIZE];
            _Buffer = new ArraySegment<byte>(buffer, 0, buffer.Length);
        }

        ArraySegment<byte> _Buffer;
        protected async Task<string> WaitMessage(CancellationToken cancellation = default)
        {
            var buffer = _Buffer;
            var array = _Buffer.Array;
            var originalSize = _Buffer.Array.Length;
            var newSize = _Buffer.Array.Length;
            try
            {
                using var linkedCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _cts.Token);
                while (true)
                {
                    var message = await socket.ReceiveAsync(buffer, linkedCancel.Token);
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
                            var o = WebsocketHelper.GetStringFromBuffer(buffer);
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
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                throw new OperationCanceledException(_cts.Token);
            }
            throw new InvalidOperationException("Should never happen");
        }

        void IDisposable.Dispose()
        {
            Dispose();
        }

        public async void Dispose()
        {
            _cts.Cancel();
            await WebsocketHelper.CloseSocket(socket);
        }

        public async Task DisposeAsync()
        {
            _cts.Cancel();
            await WebsocketHelper.CloseSocket(socket);
        }
    }
}
