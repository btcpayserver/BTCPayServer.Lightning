using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Lightning;

public static class LightningConnectionType
{
    public const string Charge = "charge";
    [Display(Name = "c-lightning")]
    public const string  CLightning= "clightning";
    [Display(Name = "LND (REST)")]
    public const string  LndREST= "lnd-rest";
    [Display(Name = "LND (gRPC)")]
    public const string LndGRPC = "lnd-grpc";
    public const string Eclair = "eclair";
    public const string LNbank = "lnbank";
    public const string LNDhub = "lndhub";
}
