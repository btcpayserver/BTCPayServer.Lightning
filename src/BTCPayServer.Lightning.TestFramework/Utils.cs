using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NBitcoin;

namespace BTCPayServer.Lightning.TestFramework
{
    public static class TestUtils
    {
        public static string PrepareDatadir(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            Directory.CreateDirectory(path);
            return path;
        }

        

        /// <summary>
        /// Ported from NBitcoin.TestFramework.NodeBuilder
        /// </summary>
        /// <param name="ports"></param>
        public static void FindPorts(int[] ports)
        {
            int i = 0;
            while (i < ports.Length)
            {
                var port = RandomUtils.GetUInt32() % 4000;
                port = port + 10000;
                if (ports.Any(p => p == port))
                    continue;
                try
                {
                    TcpListener l = new TcpListener(IPAddress.Loopback, (int)port);
                    l.Start();
                    l.Stop();
                    ports[i] = (int)port;
                    i++;
                }
                catch (SocketException) { }
            }
        }
    }
}