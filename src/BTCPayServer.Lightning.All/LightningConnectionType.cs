using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Lightning;

public static class LightningConnectionType
{
    [Display(Name = "Charge")]
    public const string Charge = "charge";
    [Display(Name = "c-lightning")]
    public const string  CLightning= "clightning";
    [Display(Name = "LND (REST)")]
    public const string  LndREST= "lnd-rest";
    [Display(Name = "LND (gRPC)")]
    public const string LndGRPC = "lnd-grpc";
    [Display(Name = "Eclair")]
    public const string Eclair = "eclair";
    [Display(Name = "LNbank")]
    public const string LNbank = "lnbank";
    [Display(Name = "LNDhub")]
    public const string LNDhub = "lndhub";
}
