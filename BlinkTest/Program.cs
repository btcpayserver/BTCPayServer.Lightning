using System.Text.Json;

using BTCPayServer.Lightning.Blink;
using BTCPayServer.Lightning.Blink.Utilities;

Uri apiUri = new Uri("https://api.blink.sv/graphql");
// put your blink API key here
string apiKey = "";

// To include your stablesats in your total balance as converted to sats
// var blinkApiClient = new BlinkApiClient(apiUri, apiKey, true);
var blinkApiClient = new BlinkApiClient(apiUri, apiKey, false);

var defaultWallet = await blinkApiClient.GetDefaultWallet(CancellationToken.None);
Console.WriteLine(defaultWallet.Id);

var totalBalance = await blinkApiClient.GetBalance(CancellationToken.None);
Console.WriteLine(totalBalance.OffchainBalance.Local);

var createIncoiceResponse = await blinkApiClient.CreateLnInvoice(
    CancellationToken.None,
    100L,
    TimeSpan.FromMinutes(10),
    "a memo to remember");

string jsonResponse = JsonSerializer.Serialize(createIncoiceResponse, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(jsonResponse);

var btcRate = await blinkApiClient.getBtcRate(CancellationToken.None);

var convertedToSats = RateConversions.convertCentsToSats(createIncoiceResponse.Amount, btcRate);
var deviation = (createIncoiceResponse.AmountReceived - convertedToSats) / createIncoiceResponse.AmountReceived * 100;

Console.WriteLine($"Invoice created with {createIncoiceResponse.Amount.MilliSatoshi} {defaultWallet.WalletCurrency}");
Console.WriteLine($"Conversion rate is {btcRate.Base} / 10^{btcRate.Offset}");
Console.WriteLine($"My own conversion: {convertedToSats} sats");
Console.WriteLine($"Received sats: {createIncoiceResponse.AmountReceived.MilliSatoshi}");
Console.WriteLine($"Deviation: %{deviation}");
