using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using NBitcoin;

namespace BTCPayServer.Lightning
{
    public class NodeInfo
    {
        public NodeInfo(PubKey nodeId, string host, int port)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            if (nodeId == null)
                throw new ArgumentNullException(nameof(nodeId));

            Port = port;
            if (IPAddress.TryParse(host, out var addr))
            {
                Host = addr.ToString();
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    Host = $"[{Host}]";
            }
            else
            {
                Host = host;
            }
            NodeId = nodeId;
        }

        public static NodeInfo Parse(string str)
        {
            if (TryParse(str, out var nodeInfo))
                return nodeInfo;
            throw new FormatException("Invalid node uri");
        }
        public static bool TryParse(string str, out NodeInfo nodeInfo)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            str = str.Trim();
            nodeInfo = null;
            var atIndex = str.IndexOf('@');
            if (atIndex == -1)
                return false;

            PubKey nodeId = null;
            try
            {
                nodeId = new PubKey(str.Substring(0, atIndex));
            }
            catch
            {
                return false;
            }

            var portIndex = str.LastIndexOf(':');
            // An ipv6 can contains two ::
            if (portIndex >= 1 && str[portIndex - 1] == ':')
                portIndex = -1;
            int port = 9735;
            string host;
            if (portIndex != -1)
            {
                if (portIndex <= atIndex)
                    return false;
                if (!int.TryParse(str.Substring(portIndex + 1), out port))
                    return false;
                host = str.Substring(atIndex + 1, portIndex - atIndex - 1);
            }
            else
            {
                host = str.Substring(atIndex + 1);
            }

            if (host.Length == 0)
                return false;
            nodeInfo = new NodeInfo(nodeId, host, port);
            return true;
        }

        public PubKey NodeId
        {
            get;
        }
        public string Host
        {
            get;
        }
        public int Port
        {
            get;
        }
        public bool IsTor
        {
            get => Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
        }
        public override string ToString()
        {
            return $"{NodeId}@{Host}:{Port}";
        }
    }
}
