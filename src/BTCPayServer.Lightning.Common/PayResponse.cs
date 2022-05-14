namespace BTCPayServer.Lightning
{
    public enum PayResult
    {
        Ok,
        CouldNotFindRoute,
        Error
    }

    public class PayResponse
    {
        public PayResponse(PayResult result)
        {
            Result = result;
        }

        public PayResponse(PayResult result, string errorDetail)
        {
            Result = result;
            ErrorDetail = errorDetail;
        }

        public PayResponse(PayResult result, PayDetails details)
        {
            Result = result;
            Details = details;
        }

        public PayResult Result { get; set; }
        public PayDetails Details { get; set; }
        public string ErrorDetail { get; set; }
    }

    public class PayDetails
    {
        public LightMoney TotalAmount { get; set; }
        public LightMoney FeeAmount { get; set; }
    }
}
