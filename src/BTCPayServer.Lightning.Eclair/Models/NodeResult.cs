using System.Collections.Generic;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public class NodeResult
    {
        public string signature { get; set; }
        public string features { get; set; }
        public int timestamp { get; set; }
        public string nodeId { get; set; }
        public string rgbColor { get; set; }
        public string alias { get; set; }
        public List<object> addresses { get; set; }
    }
}