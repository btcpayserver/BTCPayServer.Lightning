#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning
{
    /// <summary>
    /// Overall inbound-liquidity health.
    /// </summary>
    public enum LiquidityStatus
    {
        Good,
        Pending,
        Bad
    }

    /// <summary>
    /// DTO returned when liquidity could be analysed.
    /// </summary>
    public class LiquidityReport
    {
        public LiquidityStatus Liquidity_Status { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Active_Inbound_Satoshis { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Pending_Inbound_Satoshis { get; set; }
    }

    /// <summary>
    /// Helper for checking Core-Lightning inbound liquidity without introducing
    /// a compile-time dependency on BTCPayServer.Lightning.CLightning.*.
    /// </summary>
    public static class Liquidity
    {
        private static readonly LightMoney DefaultThreshold = LightMoney.Satoshis(250_000);

        /// <summary>
        /// Returns <c>null</c> when the supplied node is not Core-Lightning or
        /// if an error occurs; otherwise returns a populated <see cref="LiquidityReport"/>.
        /// </summary>
        public static async Task<LiquidityReport?> CheckAsync(
            ILightningClient client,
            ILogger?         logger      = null,
            LightMoney?      threshold   = null,
            CancellationToken token      = default)
        {
            var clientTypeName = client.GetType().Name;
            logger?.LogInformation("[Liquidity] CheckAsync started for client type: {ClientType}", clientTypeName);

            // Runtime check – keeps Common free of a direct CLightning reference.
            if (!clientTypeName.Equals("CLightningClient", StringComparison.Ordinal))
            {
                logger?.LogInformation("[Liquidity] Client is not a CLightningClient. Aborting check.");
                return null;
            }

            var min = threshold ?? DefaultThreshold;
            logger?.LogInformation("[Liquidity] Using inbound liquidity threshold of {Threshold} sats", min.MilliSatoshi / 1000);

            try
            {
                logger?.LogInformation("[Liquidity] Attempting to list channels...");
                var channels = await client.ListChannels(token);
                logger?.LogInformation("[Liquidity] Found {ChannelCount} channels.", channels.Length);
                
                // For verbose debugging, serialize the whole channel list.
                logger?.LogInformation("[Liquidity] Channels data: {ChannelsJson}", JsonConvert.SerializeObject(channels));

                // inbound capacity = total – local
                LightMoney activeInbound  = channels.Where(c => c.IsActive)
                                                    .Aggregate(LightMoney.Zero,
                                                               (s, ch) => s + (ch.Capacity - ch.LocalBalance));

                LightMoney pendingInbound = channels.Where(c => !c.IsActive)
                                                    .Aggregate(LightMoney.Zero,
                                                               (s, ch) => s + (ch.Capacity - ch.LocalBalance));
                
                logger?.LogInformation("[Liquidity] Calculated Active Inbound: {ActiveInbound} sats", activeInbound.MilliSatoshi / 1000);
                logger?.LogInformation("[Liquidity] Calculated Pending Inbound: {PendingInbound} sats", pendingInbound.MilliSatoshi / 1000);

                var status = LiquidityStatus.Bad;
                if (activeInbound >= min)
                    status = LiquidityStatus.Good;
                else if (pendingInbound >= min)
                    status = LiquidityStatus.Pending;
                
                logger?.LogInformation("[Liquidity] Determined liquidity status: {Status}", status);

                var report = new LiquidityReport
                {
                    Liquidity_Status          = status,
                    Active_Inbound_Satoshis   = activeInbound,
                    Pending_Inbound_Satoshis  = pendingInbound
                };
                
                logger?.LogInformation("[Liquidity] CheckAsync finished successfully. Returning report.");
                return report;
            }
            catch (Exception ex)
            {
                logger?.LogWarning("[Liquidity] CheckAsync failed with exception: {Exception}", ex);
                return null;
            }
        }
    }
}