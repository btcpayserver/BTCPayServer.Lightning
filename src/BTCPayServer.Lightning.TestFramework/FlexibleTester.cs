using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.LND;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.TestFramework
{
    /// <summary>
    /// Tester which launches new docker-compose instance independent from pre-launched one.
    /// It is quite heavy, so you should first consider using Tester.
    /// </summary>
    public class FlexibleTester : IDisposable
    {
        public Network Network { get; } = Network.RegTest;

        private Process p { get; set; }
        public string BuilderId { get; }
        public int InstanceId { get; }
        public string DockerComposeFilePath { get; }
        public ILogger Logger { get; }

        public int[] BitcoinPorts { get; }

        public List<FlexibleTesterActorBase> Actors { get; }
        List<IDisposable> leases = new List<IDisposable>();


        internal FlexibleTester(string builderId, int instanceId, List<FlexibleTesterActorBase> actors, string dockerComposeFilePath, ILogger logger)
        {
            BuilderId = builderId;
            InstanceId = instanceId;
            DockerComposeFilePath = dockerComposeFilePath;
            Logger = logger;
            Actors = actors;
            var baseBitcoinPorts = new int[2];
            TestUtils.FindPorts(baseBitcoinPorts);
            BitcoinPorts = baseBitcoinPorts;
            TestUtils.PrepareDatadir(GetDataDir());
        }

        private string GetDataDir() => Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), BuilderId, InstanceId.ToString()));
        private ProcessStartInfo GetStartInfo()
        {
            var info = new ProcessStartInfo();
            info.EnvironmentVariables.Add($"TESTER_BITCOIN_PORT", BitcoinPorts[0].ToString());
            info.EnvironmentVariables.Add($"TESTER_BITCOIN_RPCPORT", BitcoinPorts[1].ToString());
            info.EnvironmentVariables.Add($"TESTER_NETWORK", Network.ToString().ToLowerInvariant());
            info.EnvironmentVariables.Add($"TESTER_DATADIR", GetDataDir());
            foreach (var a in Actors)
                info = a.UpdateEnvironmentVariables(info);
            info.EnvironmentVariables["COMPOSE_PROJECT_NAME"] = $"{BuilderId}_{InstanceId.ToString()}";
            return info;
        }

        static void OutputHandler (object sendingProcess, DataReceivedEventArgs outline)
        {
            Console.WriteLine(outline.Data);
        }

        internal async Task<FlexibleTester> Launch(bool connectAll)
        {
            var startInfo = GetStartInfo();
            startInfo.FileName = "docker-compose";
            startInfo.Arguments = $" -f {DockerComposeFilePath} up";
            startInfo.UseShellExecute = false;
            startInfo.ErrorDialog = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            p = Process.Start(startInfo);

            p.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            p.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

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
            try
            {
                var addr = await client.GetDepositAddress();
                await rpc.SendToAddressAsync(addr, amount);
            }
            catch (NotSupportedException)
            {}
        }

        public async Task CheckConnectedAll()
        {
            var tasks = new List<Task>();
            foreach (var c1 in GetAllClients())
            {
                foreach (var c2 in GetAllClients())
                {
                    if (c1 != c2)
                    {
                        var task = c2.GetInfo().ContinueWith(async t => {
                            var info = await t;
                            await c1.ConnectTo(info.NodeInfo);
                        });
                        tasks.Add(task);
                    }
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
                    break;
                } catch (SwaggerException ex)
                {
                    Console.WriteLine(ex);
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
                catch (SocketException e)
                {
                    Logger?.LogDebug("Got Socket Exception");
                    Logger?.LogDebug(e.Message);
                }
                catch (WebException e)
                {
                    Logger?.LogDebug("Got Web Exception");
                    Logger?.LogDebug(e.Message);
                }
                catch (RPCException e)
                {
                    Logger?.LogDebug("Got RPC Exception");
                    Logger?.LogDebug(e.Message);
                }
                catch (JsonReaderException)
                { }
                catch (HttpRequestException e)
                {
                    Logger?.LogDebug("Got Http Request Exception");
                    Logger?.LogDebug(e.Message);
                }
                await Task.Delay(500);
            }
        }

        public IEnumerable<ILightningClient> GetAllClients()
        {
            foreach (var a in Actors)
                yield return a.GetClient();
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
            startInfo.FileName = "docker-compose";
            startInfo.Arguments = $" -f {DockerComposeFilePath} down --v";
            p = Process.Start(startInfo);
            p.WaitForExit();
        }
    }

    public class FlexibleTesterBuilder
    {
        private FlexibleTesterActorFactory ActorFactory { get; }
        private int NumBuiltTester { get; set; }

        public FlexibleTesterBuilder(Network n = null)
        {
            ActorFactory = new FlexibleTesterActorFactory(n ?? Network.RegTest);
            NumBuiltTester = 0;
        }

        public Task<FlexibleTester> BuildAsync(bool ConnectAll = true, ILogger logger = null, [CallerMemberName] string caller = null)
        {
            if (caller == null)
            {
                throw new ArgumentNullException(nameof(caller));
            }

            var composeFile = GenerateDockerCompose(caller);
            NumBuiltTester++;
            return new FlexibleTester(caller, NumBuiltTester, Actors, composeFile, logger).Launch(ConnectAll);
        }

        private List<FlexibleTesterActorBase> Actors { get; set; } = new List<FlexibleTesterActorBase>();
        public void AddNode(SupportedActorType type)
        {
            Actors.Add(ActorFactory.Create(type));
        }

        private string GetDataDir(string id) =>
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), id));

        private string GenerateDockerCompose(string id)
        {
            TestUtils.PrepareDatadir(GetDataDir(id));
            var fragmentDirectory = FindFragmentsLocation();
            var def = new DockerComposeDefinition(id);
            def.BuildOutput = Path.Combine(GetDataDir(id), "docker-compose.yml");
            def.AddFragmentFile(Path.Combine(fragmentDirectory, "main-fragment.yml"), id);
            foreach (var a in Actors)
            {
                def.AddFragmentFile(Path.Combine(fragmentDirectory, a.GetFragmentFileName()), a.ID);
            }
            def.Build();
            return def.BuildOutput;
        }

        public static string FindFragmentsLocation()
        {
            var assemblyDir = Assembly.GetExecutingAssembly().FullName;
            var path1 = Path.Combine(assemblyDir, "../../../../../src/BTCPayServer.Lightning.TestFramework/docker-fragments"); // 
            var path2 = Path.Combine(assemblyDir, "../../contentFiles/any/netstandard2.0/docker-fragments");
            if (Directory.Exists(path1))
                return path1;
            else if (Directory.Exists(path2))
                return path2;
            throw new FileNotFoundException($"docker fragments wasn't neither in \n {path1} \n nor \n {path2}");
        }
    }
}