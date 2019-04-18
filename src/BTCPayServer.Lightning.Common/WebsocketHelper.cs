using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Lightning
{
    public class WebsocketHelper
    {
        public static string ToWebsocketUri(string uri)
        {
            if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Replace("https://", "wss://");
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Replace("http://", "ws://");
            return uri;
        }

        public static async Task<ClientWebSocket> CreateClientWebSocket(string url, string authorizationValue, CancellationToken cancellation = default(CancellationToken))
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", authorizationValue);
            var uri = new UriBuilder(url) { UserName = null, Password = null }.Uri.AbsoluteUri;
            if (!uri.EndsWith("/"))
                uri += "/";
            uri += "ws";
            uri = ToWebsocketUri(uri);
            
            await socket.ConnectAsync(new Uri(uri), cancellation);
            return socket;
        }
    }
}