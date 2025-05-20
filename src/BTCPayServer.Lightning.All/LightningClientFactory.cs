using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.Eclair;
using BTCPayServer.Lightning.Phoenixd;
using BTCPayServer.Lightning.LNbank;
using BTCPayServer.Lightning.LND;
using BTCPayServer.Lightning.LNDhub;
using NBitcoin;

namespace BTCPayServer.Lightning;

public class LightningClientFactory : ILightningClientFactory
{
    public static readonly IReadOnlyList<ILightningConnectionStringHandler> DefaultHandlers =
        new ILightningConnectionStringHandler[]
        {
            new ChargeLightningConnectionStringHandler(), new CLightningConnectionStringHandler(),
            new EclairConnectionStringHandler(), new PhoenixdConnectionStringHandler(),
            new LndConnectionStringHandler(), new LndHubConnectionStringHandler(),
            new LNbankConnectionStringHandler()
        };

    private readonly Network _network;
    private readonly ILightningConnectionStringHandler[] _connectionStringHandlers;

    public LightningClientFactory(
        Network network) : this(DefaultHandlers, network)
    {
    }


    public LightningClientFactory(IEnumerable<ILightningConnectionStringHandler> connectionStringHandlers,
        Network network)
    {
        _network = network;
        _connectionStringHandlers = connectionStringHandlers.ToArray();
    }

    public ILightningClient Create(string connectionString)
    {
        if (connectionString == null)
            throw new ArgumentNullException(nameof(connectionString));
        FormatException lastError = null;
        foreach (var handler in _connectionStringHandlers)
        {
            try
            {
                var client = handler.Create(connectionString, _network, out var error);
                if (client != null)
                {
                    return client;
                }
                if (error is not null)
                {
                    throw new FormatException(error);
                }
            }
            catch (FormatException e)
            {
                lastError = e;
            }
        }
        if(lastError is not null)
            throw lastError;

        throw new NotSupportedException(
            $"Unsupported connection string");
    }

    public bool TryCreate(string connectionString, out ILightningClient client, out string error)
    {
        try
        {
            client= Create(connectionString);
            error = null;
            return true;
        }
        catch (Exception e)
        {
            client = null;
            error = e.Message;
            return false;
        }
    }
}
