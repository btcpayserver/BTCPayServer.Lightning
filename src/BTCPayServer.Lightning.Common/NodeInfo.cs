using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public class NodeInfo
    {
        public NodeInfo(PubKey nodeId, string host, int port)
        {
            if(host == null)
                throw new ArgumentNullException(nameof(host));
            if(nodeId == null)
                throw new ArgumentNullException(nameof(nodeId));
            Port = port;
            Host = host;
            NodeId = nodeId;
        }
        public static bool TryParse(string str, out NodeInfo nodeInfo)
        {
            if(str == null)
                throw new ArgumentNullException(nameof(str));
            str = str.Trim();
            nodeInfo = null;
            var atIndex = str.IndexOf('@');
            if(atIndex == -1)
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

            var portIndex = str.IndexOf(':');
            int port = 9735;
            if(portIndex != -1)
            {
                if(portIndex <= atIndex)
                    return false;
                if(!int.TryParse(str.Substring(portIndex + 1), out port))
                    return false;
            }


            string host = str.Substring(atIndex + 1, portIndex - atIndex - 1);
            if(host.Length == 0)
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
