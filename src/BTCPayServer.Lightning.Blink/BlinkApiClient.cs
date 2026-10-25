using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;

using BTCPayServer.Lightning.Blink.Models.Responses;
using BTCPayServer.Lightning.Blink.Models;
using BTCPayServer.Lightning.Blink.Utilities;

namespace BTCPayServer.Lightning.Blink;

public class BlinkApiClient
{
    private Uri _baseUri;
    private string _apiKey;
    private bool _includeStablesats;

    public BlinkApiClient(Uri baseUri, string apiKey, bool includeStablesats = false)
    {
        _baseUri = baseUri;
        _apiKey = apiKey;
        _includeStablesats = includeStablesats;
    }

    public async Task<BlinkWallet> GetDefaultWallet(CancellationToken cancellationToken)
    {
        string query = @"
            query GetDefaultWallet {
                me {
                    defaultAccount {
                        defaultWalletId
                        wallets {
                            balance
                            id
                            walletCurrency
                        }
                    }
                }
            }";

        var defaultWalletRequest = new GraphQLRequest
        {
            Query = query,
            OperationName = "GetDefaultWallet"
        };

        var response = await getGraphQLHttpClient().SendQueryAsync<GetDefaultWalletIdResponse>(defaultWalletRequest, cancellationToken);
        string defaultWalletId = response.Data.Me.DefaultAccount.DefaultWalletId;
        var defaultWallet = response.Data.Me.DefaultAccount.Wallets.Find((w) => w.Id == defaultWalletId);

        if (defaultWallet == null)
        {
            throw new Exception(string.Format("Could not find wallet information for wallet id {0}", defaultWalletId));
        }

        return new BlinkWallet
        {
            Id = defaultWallet.Id,
            Balance = defaultWallet.Balance,
            WalletCurrency = defaultWallet.WalletCurrency
        };
    }

    public async Task<PriceInfo> getBtcRate(CancellationToken cancellationToken)
    {
        string query = @"
            query RealtimePrice($currency: DisplayCurrency) {
              realtimePrice(currency: $currency) {
                btcSatPrice {
                  base,
                  offset
                }
              }
            }";

        var getBtcRateRequest = new GraphQLRequest
        {
            Query = query,
            Variables = new
            {
                input = new
                {
                    currency = "USD"
                }
            }
        };

        var response = await getGraphQLHttpClient().SendQueryAsync<GetBtcRateResponse>(getBtcRateRequest, cancellationToken);

        return response.Data.RealtimePrice.BtcSatPrice;
    }

    public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellationToken)
    {
        string realtimePriceQuery = _includeStablesats
            ? @"realtimePrice {
                    btcSatPrice {
                      base
                      offset
                    }
                    denominatorCurrency
                  }"
            : "";

        string query = $@"
            query GetWalletBalances {{
                me {{
                    defaultAccount {{
                      defaultWalletId,
                      wallets {{
                        balance
                        id
                        walletCurrency
                      }}
                    }}
                }}
                {realtimePriceQuery}
            }}";

        var balancesRequest = new GraphQLRequest
        {
            Query = query,
            OperationName = "GetWalletBalances"
        };

        var response = await getGraphQLHttpClient().SendQueryAsync<GetWalletBalancesResponse>(balancesRequest, cancellationToken);
        var responseData = response.Data;

        long totalBalanceSats = responseData.Me.DefaultAccount.Wallets.Sum(wallet =>
        {
            if (wallet.WalletCurrency == "USD")
            {
                var realtimePrice = responseData.RealtimePrice;

                if (_includeStablesats
                        && realtimePrice != null
                        && realtimePrice.BtcSatPrice != null
                        && realtimePrice.DenominatorCurrency == "USD")
                {
                    return RateConversions.convertCentsToSats(wallet.Balance, realtimePrice.BtcSatPrice);
                }

                return 0;
            } else
            {
                return wallet.Balance;
            }
        });

        var offchain = new OffchainBalance
        {
            Local = new LightMoney(totalBalanceSats, LightMoneyUnit.MilliSatoshi)
        };

        return new LightningNodeBalance(null, offchain);
    }

    public async Task<LightningInvoice> CreateLnInvoice(CancellationToken cancellationToken, long amount, TimeSpan expiresIn, string? memo = null)
    {
        var wallet = await this.GetDefaultWallet(cancellationToken);

        string apiCall = wallet.WalletCurrency == "USD" ? "lnUsdInvoiceCreate" : "LnInvoiceCreate";

        string query = $@"
            mutation {apiCall}($input: {apiCall.Capitalize()}Input!) {{
              {apiCall}(input: $input) {{
                invoice {{
                  paymentRequest
                  paymentHash
                  paymentSecret
                  satoshis
                }}
                errors {{
                  message
                }}
              }}
            }}";

        var createLnInvoiceRequest = new GraphQLRequest
        {
            Query = query,
            OperationName = apiCall,
            Variables = new
            {
                input = new
                {
                    amount,
                    walletId = wallet.Id,
                    expiresIn,
                    memo
                }
            }
        };

        var response = await getGraphQLHttpClient().SendMutationAsync<LnInvoiceCreateResponse>(createLnInvoiceRequest, cancellationToken);
        var graphQlErrors = response.Errors;

        if (graphQlErrors != null && graphQlErrors.Length > 0)
        {
            var messages = string.Join(", ", graphQlErrors.Select(e => e.Message));
            throw new Exception($"An error occured with invoice creation query: {messages}");
        }

        var invoiceData = response.Data.LnInvoiceCreate != null
            ? response.Data.LnInvoiceCreate
            : response.Data.LnUsdInvoiceCreate;

        if (invoiceData.Errors.Count > 0)
        {
            var messages = string.Join(", ", invoiceData.Errors.Select(e => e.Message));
            throw new Exception($"An error occured with invoice creation: {messages}");
        }

        return new LightningInvoice
        {
            Amount = amount,
            PaymentHash = invoiceData.Invoice.PaymentHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiresIn),
            AmountReceived = invoiceData.Invoice.Satoshis,
            Status = LightningInvoiceStatus.Unpaid,
            BOLT11 = invoiceData.Invoice.PaymentRequest,
            Preimage = invoiceData.Invoice.PaymentSecret
        };
    }

    private GraphQLHttpClient getGraphQLHttpClient() {
        var graphQLClient = new GraphQLHttpClient(_baseUri, new NewtonsoftJsonSerializer());

        graphQLClient.HttpClient.DefaultRequestHeaders.Clear();
        graphQLClient.HttpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);
        graphQLClient.HttpClient.DefaultRequestHeaders.Add("User-Agent", "BTCPayServer.Lightning.BlinkApiClient");

        return graphQLClient;
    }
}
