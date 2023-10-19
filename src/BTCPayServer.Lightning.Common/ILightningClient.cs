using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Lightning
{
    public interface ILightningClient
    {
        Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default);
        Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default);
        Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default);
        Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default);
        Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default);
        Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default);
        Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default);
        Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default);
        Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = default);
        Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default);
        Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default);
        Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default);
        Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default);
        Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default);
        Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default);
        Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default);
        Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default);
        Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default);
        Task CancelInvoice(string invoiceId, CancellationToken cancellation = default);
        Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default);

    }

    public interface ILightningInvoiceListener : IDisposable
    {
        Task<LightningInvoice> WaitInvoice(CancellationToken cancellation);
    }

    public interface ILightningConnectionStringHandler
    {
        ILightningClient Create(string connectionString, Network network, out string error);
        
    }

    public static class LightningConnectionStringHelper
    {
        public static Dictionary<string, string> ExtractValues(string connectionString, out string type)
        {
            if (!TryParseLegacy(connectionString, out var keyValues))
            {
                var parts = connectionString.Split(new [] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                keyValues = new Dictionary<string, string>();
                foreach (var part in parts.Select(p => p.Trim()))
                {
                    var idx = part.IndexOf('=');
                    if (idx == -1)
                    {
                        throw new FormatException("The format of the connectionString should a list of key=value delimited by semicolon");
                    }
                    var key = part.Substring(0, idx).Trim().ToLowerInvariant();
                    var value = part.Substring(idx + 1).Trim();
                    if (keyValues.ContainsKey(key))
                    {
                        throw new FormatException($"Duplicate key {key}");
                    }
                    keyValues.Add(key, value);
                }
            }
            if (!keyValues.TryGetValue("type", out type))
            {
                throw new FormatException("The key 'type' is mandatory");
            }
            return keyValues;
        }


        public static  bool VerifySecureEndpoint(Uri uri, bool allowInsecure)
        {
            return uri.Scheme== "https" || allowInsecure || uri.Host.EndsWith("onion");
        }
        
         private static bool TryParseLegacy(string str, out Dictionary<string, string> connectionString)
        {
            if (str.StartsWith("/"))
                str = "unix:" + str;
            var result = new Dictionary<string, string>();
            connectionString = null;

            Uri uri;
            if (!Uri.TryCreate(str, UriKind.Absolute, out uri))
            {
                return false;
            }

            var supportedDomains = new string[] { "unix", "tcp", "http", "https" };
            if (!supportedDomains.Contains(uri.Scheme))
            {
                return false;
            }
            if (uri.Scheme == "unix")
            {
                str = uri.AbsoluteUri.Substring("unix:".Length);
                while (str.Length >= 1 && str[0] == '/')
                {
                    str = str.Substring(1);
                }
                uri = new Uri("unix://" + str, UriKind.Absolute);
                result.Add("type", "clightning");
            }

            if (uri.Scheme == "tcp")
                result.Add("type", "clightning");

            if (uri.Scheme == "http" || uri.Scheme == "https")
            {
                var parts = uri.UserInfo.Split(':');
                if (string.IsNullOrEmpty(uri.UserInfo) || parts.Length != 2)
                {
                    return false;
                }
                result.Add("type", "charge");
                result.Add("username", parts[0]);
                result.Add("password", parts[1]);
                if (uri.Scheme == "http")
                    result.Add("allowinsecure", "true");
            }
            else if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                return false;
            }
            result.Add("server",new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString());
            connectionString = result;
            return true;
        }
    }
}
