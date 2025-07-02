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
            // Runtime check – keeps Common free of a direct CLightning reference.
            if (!client.GetType().Name.Equals("CLightningClient", StringComparison.Ordinal))
                return null;

            var min = threshold ?? DefaultThreshold;

            try
            {
                var channels = await client.ListChannels(token);

                // inbound capacity = total – local
                LightMoney activeInbound  = channels.Where(c => c.IsActive)
                                                    .Aggregate(LightMoney.Zero,
                                                               (s, ch) => s + (ch.Capacity - ch.LocalBalance));

                LightMoney pendingInbound = channels.Where(c => !c.IsActive)
                                                    .Aggregate(LightMoney.Zero,
                                                               (s, ch) => s + (ch.Capacity - ch.LocalBalance));

                var status = LiquidityStatus.Bad;
                if (activeInbound >= min)
                    status = LiquidityStatus.Good;
                else if (pendingInbound >= min)
                    status = LiquidityStatus.Pending;

                return new LiquidityReport
                {
                    Liquidity_Status          = status,
                    Active_Inbound_Satoshis   = activeInbound,
                    Pending_Inbound_Satoshis  = pendingInbound
                };
            }
            catch (Exception ex)
            {
                // Works with the ILogger overloads available in netstandard2.0.
                logger?.LogWarning("[Liquidity] check failed: {Exception}", ex);
                return null;
            }
        }
    }
}