using System;
using System.Net.Http;
using NBitcoin;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankClient
    {
        private readonly Uri _address;
        private readonly string _apiToken;
        private readonly Network _network;
        private readonly HttpClient _httpClient;

        public LNbankClient(Uri address, string apiToken, Network network, HttpClient httpClient = null)
        {
            _address = address;
            _apiToken = apiToken;
            _network = network;
            _httpClient = httpClient;
        }
    }
}
