using System.Collections.Generic;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class AllNodesResponse
    {
        public string Signature { get; set; }
        public string Features { get; set; }
        public long Timestamp { get; set; }
        public string NodeId { get; set; }
        public string RgbColor { get; set; }
        public string Alias { get; set; }
        public List<string> Addresses { get; set; }
    }
}