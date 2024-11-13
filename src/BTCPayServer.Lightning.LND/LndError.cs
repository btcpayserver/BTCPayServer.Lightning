namespace BTCPayServer.Lightning.LND
{
    public class LNDError
    {
        public string Error { get; set; }
        public string Message { get; set; }
        public int Code { get; set; }
    }
    
    class LNDNestedError
    {
        public LNDError Error { get; set; }
    }
}
