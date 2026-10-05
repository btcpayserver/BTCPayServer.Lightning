using System;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Lightning
{
    public class WebsocketHelper
    {
        static UTF8Encoding UTF8 = new UTF8Encoding(false, true);
        public const int ORIGINAL_BUFFER_SIZE = 1024 * 5;
        public const int MAX_BUFFER_SIZE = 1024 * 1024 * 5;
        public static string ToWebsocketUri(string uri)
        {
            if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Replace("https://", "wss://");
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Replace("http://", "ws://");
            return uri;
        }

        public static async Task<ClientWebSocket> CreateClientWebSocket(string url, string authorizationValue, CancellationToken cancellation = default)
        {
            return await CreateClientWebSocket(url, authorizationValue, cancellation, null);
        }

        public static async Task<ClientWebSocket> CreateClientWebSocket(string url, string authorizationValue, CancellationToken cancellation, HttpClient httpClient)
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", authorizationValue);
            var uri = new UriBuilder(url) { UserName = null, Password = null }.Uri.AbsoluteUri;
            if (!uri.EndsWith("/"))
                uri += "/";
            uri += "ws";
            uri = ToWebsocketUri(uri);

            var websocketUri = new Uri(uri);
            try
            {
#if NET10_0_OR_GREATER
                if (httpClient is not null)
                    await socket.ConnectAsync(websocketUri, httpClient, cancellation);
                else
#endif
                    await socket.ConnectAsync(websocketUri, cancellation);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public static async Task CloseSocket(WebSocket webSocket, WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string description = null, CancellationToken cancellationToken = default)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    cts.CancelAfter(5000);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, description ?? "Closing", cts.Token);
                }
            }
            catch { }
            finally { try { webSocket.Dispose(); } catch { } }
        }

        public static async Task CloseSocketAndThrow(ArraySegment<byte> buffer, ClientWebSocket socket, WebSocketCloseStatus status, string description, CancellationToken cancellation)
        {
            var array = buffer.Array;
            if (array.Length != WebsocketHelper.ORIGINAL_BUFFER_SIZE)
                Array.Resize(ref array, WebsocketHelper.ORIGINAL_BUFFER_SIZE);
            await WebsocketHelper.CloseSocket(socket, status, description, cancellation);
            throw new WebSocketException($"The socket has been closed ({status}: {description})");
        }

        public static string GetStringFromBuffer(ArraySegment<byte> buffer)
        {
            return UTF8.GetString(buffer.Array, 0, buffer.Count);
        }
    }
}
