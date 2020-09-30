namespace BTCPayServer.Lightning
{
    public enum CloseChannelResult
    {
        Ok,
        Failed,
    }

    public class CloseChannelResponse
    {
        public CloseChannelResponse(CloseChannelResult result)
        {
            Result = result;
        }
        public CloseChannelResult Result
        {
            get; set;
        }
    }
}
