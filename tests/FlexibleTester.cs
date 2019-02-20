using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.LND;
using BTCPayServer.Lightning.Tests;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Tests
{
    /// <summary>
    /// Tester which launches new docker-compose instance independent from pre-launched one.
    /// It is quite heavy, so you should first consider using Tester.
    /// Currently it does not support clightning-charge because there was no need.
    /// </summary>
    public class FlexibleTester : IDisposable
    {
        private int[] BitcoinPorts = new int[2];
        private int CLightningPort { get; }
        private int LndPort { get; }
        private int CLightningDestPort { get; }

        private int LndDestPort { get; }

        public Network Network { get; } = Network.RegTest;

        private Process p { get; set; }
        public string InstanceId { get; }
        public string DockerComposeFilePath { get; }

        internal FlexibleTester(string instanceId, int[] ports, string dockerComposeFilePath)
        {
            InstanceId = instanceId;
            DockerComposeFilePath = dockerComposeFilePath;
            BitcoinPorts = ports.Take(2).ToArray();
            CLightningPort = ports[2];
            CLightningPort = ports[3];
            LndPort = ports[4];
            CLightningDestPort = ports[5];
            LndDestPort = ports[6];
            PrepareDatadir();
        }

        private string GetDataDir() => Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), InstanceId));
        private string PrepareDatadir()
        {
            var path = GetDataDir();
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            Directory.CreateDirectory(path);
            return path;
        }


        private ProcessStartInfo GetStartInfo()
        {
            var info = new ProcessStartInfo();
            info.EnvironmentVariables.Add($"TESTER_BITCOIN_PORT", BitcoinPorts[0].ToString());
            info.EnvironmentVariables.Add($"TESTER_BITCOIN_RPCPORT", BitcoinPorts[1].ToString());
            info.EnvironmentVariables.Add($"TESTER_LND_PORT", LndPort.ToString());
            info.EnvironmentVariables.Add($"TESTER_LND_DEST_PORT", LndDestPort.ToString());
            info.EnvironmentVariables.Add($"TESTER_CLIGHTNING_PORT", CLightningPort.ToString());
            info.EnvironmentVariables.Add($"TESTER_CLIGHTNING_DEST_PORT", CLightningDestPort.ToString());
            info.EnvironmentVariables.Add($"TESTER_NETWORK", Network.ToString().ToLowerInvariant());
            info.EnvironmentVariables.Add($"TESTER_DATADIR", GetDataDir());

            return info;
        }

        static void OutputHandler (object sendingProcess, DataReceivedEventArgs outline)
        {
            Console.WriteLine(outline.Data);
        }

        internal async Task<FlexibleTester> Launch(bool connectAll, bool debug = false)
        {
            var startInfo = GetStartInfo();
            startInfo.EnvironmentVariables["COMPOSE_PROJECT_NAME"] = InstanceId;
            startInfo.FileName = "docker-compose";
            startInfo.Arguments = $" -f {DockerComposeFilePath} up";
            startInfo.UseShellExecute = false;
            startInfo.ErrorDialog = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            p = Process.Start(startInfo);

            //--
            if (debug)
            {
                p.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
                p.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            // LND will throw 500 error when tried to connect if it has no scanned block in it.
            await CheckNodeStartedAll();
            if (connectAll)
                await CheckConnectedAll();
            else
                await Task.Delay(1000); // to assure (in ugly way) that the scan is finished and the consumer won't face an error when tried to connect.
            return this;
        }

        public RPCClient CreateRPC()
        {
            return new RPCClient("ceiwHEbqWI83:DwubwWsoo3", $"127.0.0.1:{BitcoinPorts[1]}", Network);
        }

        public LndClient CreateLndClient()
        {
            return new LndClient(new LndRestSettings()
            {
                AllowInsecure = true,
                Uri = new Uri($"https://127.0.0.1:{LndPort}")
            }, Network.RegTest);
        }

        public CLightningClient CreateCLightningClient()
        {
            return new CLightningClient(new Uri($"tcp://127.0.0.1:{CLightningPort}"), Network);
        }

        public CLightningClient CreateCLightningClientDest()
        {
            return new CLightningClient(new Uri($"tcp://127.0.0.1:{CLightningDestPort}"), Network);
        }

        public LndClient CreateLndClientDest()
        {
            return new LndClient(new LndRestSettings()
            {
                AllowInsecure = true,
                Uri = new Uri($"https://127.0.0.1:{LndDestPort}")
            }, Network.RegTest);
        }

        public async Task PrepareFunds(decimal amountSatoshi = 10000000m, int conf = 10, ILightningClient onlyThisClient = null)
        {
            var amount = Money.Satoshis(amountSatoshi);
            var rpc = CreateRPC();
            var height = await rpc.GetBlockCountAsync();
            if (height <= Network.Consensus.CoinbaseMaturity)
                await rpc.GenerateAsync(1 + Network.Consensus.CoinbaseMaturity);
            if (onlyThisClient != null)
                await PrepareFundsCore(rpc, onlyThisClient, amount);
            else
            {
                await Task.WhenAll(GetAllClients().Select(c => PrepareFundsCore(rpc, c, amount)));
            }
            await rpc.GenerateAsync(conf);
        }

        public async Task PrepareFundsCore(RPCClient rpc, ILightningClient client, Money amount)
        {
            var addr = await client.GetDepositAddress();
            await rpc.SendToAddressAsync(addr, amount);
        }

        public async Task CheckConnectedAll()
        {
            var tasks = new List<Task>();
            foreach (var sender in GetLightningSenderClients())
            {
                foreach (var dest in GetLightningDestClients())
                {
                    tasks.Add(CheckConnected(sender, dest));
                }
            }
            await Task.WhenAll(tasks);
        }

        async Task CheckConnected(ILightningClient sender, ILightningClient dest)
        {
            while (true)
            {
                try
                {
                    var info = await dest.GetInfo();
                    await sender.ConnectTo(info.NodeInfo);
                } catch (SwaggerException)
                {
                }
                await Task.Delay(1000);
            }
        }
        private async Task CheckNodeStartedAll()
        {
            var tasks = new List<Task>();
            var rpc = CreateRPC();
            Console.WriteLine("Checking Bitcoin RPC");
            await CheckNodeStarted(rpc);
            Console.WriteLine("Finished Checking Bitcoin");
            await CreateRPC().GenerateAsync(1);
            foreach (var c in GetAllClients())
                tasks.Add(CheckNodeStarted(c));
            await Task.WhenAll(tasks);
        }

        private Task CheckNodeStarted(ILightningClient client)
            => CheckNodeStartedCore(() => client.GetInfo());

        private Task CheckNodeStarted(RPCClient client)
            => CheckNodeStartedCore(() => client.GetBlockchainInfoAsync());

        private async Task CheckNodeStartedCore(Func<Task> checkingMethod)
        {
            while (true)
            {
                try
                {
                    await checkingMethod();
                    break;
                }
                catch (SocketException)
                { }
                catch (WebException)
                { }
                catch (RPCException)
                { }
                await Task.Delay(500);
            }
        }

        public IEnumerable<ILightningClient> GetLightningSenderClients()
        {
            yield return CreateCLightningClient();
            yield return CreateLndClient();
        }
        public IEnumerable<ILightningClient> GetLightningDestClients()
        {
            yield return CreateCLightningClientDest();
            yield return CreateLndClientDest();
        }

        public IEnumerable<ILightningClient> GetAllClients()
        {
            foreach (var sender in GetLightningSenderClients())
                yield return sender;
            foreach (var dest in GetLightningDestClients())
                yield return dest;
        }

        public void Dispose()
        {
            if (p != null && !p.HasExited)
            {
                RunDockerComposeDown();
            }
        }
        
        private void RunDockerComposeDown()
        {
            var startInfo = GetStartInfo();
            startInfo.EnvironmentVariables["COMPOSE_PROJECT_NAME"] = InstanceId;
            startInfo.FileName = "docker-compose";
            startInfo.Arguments = $" -f {DockerComposeFilePath} down";
            p = Process.Start(startInfo);
            p.WaitForExit();
        }
    }

    public class FlexibleTesterBuilder
    {
        /// <summary>
        /// Ported from NBitcoin.TestFramework.NodeBuilder
        /// </summary>
        /// <param name="ports"></param>
        private static void FindPorts(int[] ports)
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

        // TODO: consider the case for this library is used from different assembly?
        private static string GetComposeFilePath()
        {
            var path1 = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../../../docker-compose.flexible.yml"));
            if (!File.Exists(path1))
                throw new InvalidOperationException($"Could not found docker-compose file {path1}");
            return path1;
        }
 
        public static Task<FlexibleTester> CreateAsync(bool ConnectAll = true, bool debugDockerProcess = false, [CallerMemberName] string caller =  null)
        {
            if (caller == null)
            {
                throw new ArgumentNullException(nameof(caller));
            }
            var ports = new int[7];
            FindPorts(ports);
            var composeFile = GetComposeFilePath();
            return new FlexibleTester(caller, ports, composeFile).Launch(ConnectAll, debugDockerProcess);
        }

    }
}