using System;
using NBitcoin;

namespace BTCPayServer.Lightning.CLightning;

public class CLightningConnectionStringHandler : ILightningConnectionStringHandler
{
    public ILightningClient Create(string connectionString, Network network, out string error)
    {
        
        
        
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "clightning")
        {
            error = null;
            return null;
        }
        
        if (!kv.TryGetValue("server", out var server))
        {
            error = $"The key 'server' is mandatory for clightning connection strings";
            return null;
        }
        

        if (server.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            server = "unix:" + server;
        else if (server.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            server = "unix:/" + server;

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri)
            || (uri.Scheme != "tcp" && uri.Scheme != "unix"))
        {
            error = $"The key 'server' should be an URI starting by tcp:// or unix:// or a path to the 'lightning-rpc' unix socket";
            return null;
        }

        error = null;
        return new CLightningClient(uri, network);
    }
    
    
}
