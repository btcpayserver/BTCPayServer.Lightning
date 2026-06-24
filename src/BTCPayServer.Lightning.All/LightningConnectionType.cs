using System;

namespace BTCPayServer.Lightning;

[Obsolete]
public static class LightningConnectionType
{
    public const string  CLightning= "clightning";
    public const string  LndREST= "lnd-rest";
    public const string LndGRPC = "lnd-grpc";
    public const string Eclair = "eclair";
    public const string LNDhub = "lndhub";
}
