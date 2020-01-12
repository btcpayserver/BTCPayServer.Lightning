using NBitcoin;
using System.Linq;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace BTCPayServer.Lightning.Tests
{
    /// <summary>
    /// Utilities to connect channels on regtest 
    /// </summary>
    public static class ConnectChannels
    {
        /// <summary>
        /// Create channels from all senders to all receivers while mining on cashCow
        /// </summary>
        /// <param name="cashCow">The miner and liquidity source</param>
        /// <param name="senders">Senders of payment on Lightning Network</param>
        /// <param name="receivers">Receivers of payment on Lightning Network</param>
        /// <returns></returns>
        public static async Task ConnectAll(RPCClient cashCow, IEnumerable<ILightningClient> senders, IEnumerable<ILightningClient> receivers)
        {
            if(await cashCow.GetBlockCountAsync() <= cashCow.Network.Consensus.CoinbaseMaturity)
            {
                await cashCow.GenerateAsync(cashCow.Network.Consensus.CoinbaseMaturity + 1);
            }


            foreach(var sender in senders)
            {
                foreach(var dest in receivers)
                {
                    await CreateChannel(cashCow, sender, dest);
                }
            }
        }
        public static async Task CreateChannel(RPCClient cashCow, ILightningClient sender, ILightningClient dest)
        {
            var destInfo = await dest.GetInfo();
            var destInvoice = await dest.CreateInvoice(1000, "EnsureConnectedToDestination", TimeSpan.FromSeconds(5000));
            while(true)
            {
                var result = await Pay(sender, destInvoice.BOLT11);
                if(result.Result == PayResult.CouldNotFindRoute)
                {
                    var openChannel = await sender.OpenChannel(new OpenChannelRequest()
                    {
                        NodeInfo = destInfo.NodeInfoList[0],
                        ChannelAmount = Money.Satoshis(16777215),
                        FeeRate = new FeeRate(1UL, 1)
                    });
                    if(openChannel.Result == OpenChannelResult.CannotAffordFunding)
                    {
                        var address = await sender.GetDepositAddress();
						try
						{
							await cashCow.SendToAddressAsync(address, Money.Coins(1.0m), null, null, true);
						}
						catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_WALLET_INSUFFICIENT_FUNDS || ex.RPCCode == RPCErrorCode.RPC_WALLET_ERROR)
						{
							await cashCow.GenerateAsync(1);
							await cashCow.SendToAddressAsync(address, Money.Coins(1.0m), null, null, true);
						}
                        await cashCow.GenerateAsync(10);
                        await WaitLNSynched(cashCow, sender);
                        await WaitLNSynched(cashCow, dest);
                    }
                    if(openChannel.Result == OpenChannelResult.PeerNotConnected)
                    {
                        await sender.ConnectTo(destInfo.NodeInfoList[0]);
                    }
                    if(openChannel.Result == OpenChannelResult.NeedMoreConf)
                    {
                        await cashCow.GenerateAsync(6);
                        await WaitLNSynched(cashCow, sender);
                        await WaitLNSynched(cashCow, dest);
                    }
                    if(openChannel.Result == OpenChannelResult.AlreadyExists)
                    {
                        await Task.Delay(1000);
                    }
                }
                else if(result.Result == PayResult.Ok)
                {
                    break;
                }
            }
        }

        private static async Task<PayResponse> Pay(ILightningClient sender, string payreq)
        {
            using (var cts = new CancellationTokenSource(5000))
            {
            retry:
                try
                {
                    return await sender.Pay(payreq);
                }
                catch (CLightning.LightningRPCException ex) when (ex.Message.Contains("WIRE_INCORRECT_OR_UNKNOWN_PAYMENT_DETAILS") &&
                                                                  !cts.IsCancellationRequested)
                {
                    await Task.Delay(500);
                    goto retry;
                }
            }
        }

        private static async Task<LightningNodeInformation> WaitLNSynched(RPCClient rpc, ILightningClient lightningClient)
        {
            while(true)
            {
                var merchantInfo = await lightningClient.GetInfo();
                var blockCount = await rpc.GetBlockCountAsync();
                if(merchantInfo.BlockHeight != blockCount)
                {
                    await Task.Delay(500);
                }
                else
                {
                    return merchantInfo;
                }
            }
        }
    }
}
