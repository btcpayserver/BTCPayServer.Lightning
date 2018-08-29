using System;
using System.Threading.Tasks;
using Xunit;
using System.Linq;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Lightning.Tests
{
    public class CommonTests
    {
        [Fact]
        public async Task CanCreateInvoice()
        {
            foreach(var client in Tester.GetLightningClients())
            {
                var createdInvoice = await client.CreateInvoice(10000, "CanCreateInvoice", TimeSpan.FromMinutes(5));
                var retrievedInvoice = await client.GetInvoice(createdInvoice.Id);
                AssertUnpaid(createdInvoice);
                AssertUnpaid(retrievedInvoice);
            }
        }

        [Fact]
        public async Task CanGetInfo()
        {
            await EnsureConnectedToDestinations();
            var blockHeight = Tester.CreateRPC().GetBlockCount();
            foreach(var client in Tester.GetLightningClients())
            {
                var info = await client.GetInfo();
                Assert.NotNull(info);
                Assert.Equal(blockHeight, info.BlockHeight);
                Assert.NotNull(info.NodeInfo);
            }
        }

        [Fact]
        public async Task CanPayInvoiceAndReceive()
        {
            await EnsureConnectedToDestinations();

            foreach(var client in Tester.GetLightningSenderClients())
            {
                foreach(var dest in Tester.GetLightningDestClients())
                {
                    var invoice = await dest.CreateInvoice(10000, "CanPayInvoiceAndReceive", TimeSpan.FromSeconds(5000));
                    using(var listener = await dest.Listen())
                    {
                        var waiting = listener.WaitInvoice(default);
                        var paidReply = await client.Pay(invoice.BOLT11);
                        Assert.Equal(PayResult.Ok, paidReply.Result);
                        var paidInvoice = await waiting;
                        Assert.Equal(LightningInvoiceStatus.Paid, paidInvoice.Status);
                        var retrievedInvoice = await dest.GetInvoice(invoice.Id);
                        Assert.Equal(LightningInvoiceStatus.Paid, retrievedInvoice.Status);
                    }
                }
            }
        }

        private static void AssertUnpaid(LightningInvoice invoice)
        {
            Assert.NotNull(invoice.BOLT11);
            Assert.Equal(LightMoney.MilliSatoshis(10000), invoice.Amount);
            Assert.Null(invoice.PaidAt);
            Assert.Equal(LightningInvoiceStatus.Unpaid, invoice.Status);
        }

        private async Task EnsureConnectedToDestinations()
        {

            var cashCow = Tester.CreateRPC();
            if(await cashCow.GetBlockCountAsync() <= Tester.Network.Consensus.CoinbaseMaturity)
            {
                await cashCow.GenerateAsync(Tester.Network.Consensus.CoinbaseMaturity + 1);
            }


            foreach(var sender in Tester.GetLightningSenderClients())
            {
                foreach(var dest in Tester.GetLightningDestClients())
                {
                    var destInfo = await dest.GetInfo();
                    var destInvoice = await dest.CreateInvoice(1000, "EnsureConnectedToDestination", TimeSpan.FromSeconds(5000));
                    while(true)
                    {
                        var result = await sender.Pay(destInvoice.BOLT11);
                        if(result.Result == PayResult.CouldNotFindRoute)
                        {
                            var openChannel = await sender.OpenChannel(destInfo.NodeInfo, Money.Satoshis(16777215));
                            if(openChannel.Result == OpenChannelResult.CannotAffordFunding)
                            {
                                var address = await sender.GetDepositAddress();
                                await cashCow.SendToAddressAsync(address, Money.Coins(1.0m));
                                await cashCow.GenerateAsync(10);
                                await WaitLNSynched(cashCow, sender);
                                await WaitLNSynched(cashCow, dest);
                            }
                            if(openChannel.Result == OpenChannelResult.PeerNotConnected)
                            {
                                await sender.ConnectTo(destInfo.NodeInfo);
                            }
                            if(openChannel.Result == OpenChannelResult.NeedMoreConf)
                            {
                                await cashCow.GenerateAsync(6);
                                await WaitLNSynched(cashCow, sender);
                                await WaitLNSynched(cashCow, dest);
                            }
                        }
                        else if(result.Result == PayResult.Ok)
                        {
                            break;
                        }
                    }
                }
            }
        }

        private async Task<LightningNodeInformation> WaitLNSynched(RPCClient rpc, ILightningClient lightningClient)
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
