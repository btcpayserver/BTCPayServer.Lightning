using System;
using System.Net.Http;
using NBitcoin;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankClient
    {
        private readonly string _walletId;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializer _serializer;
        private readonly Network _network;

        public LNbankClient(Uri baseUri, string apiToken, string walletId, Network network, HttpClient httpClient)
        {
            _walletId = walletId;
            _httpClient = httpClient;
            _network = network;
        }
    }
}
