using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning.LND
{
    internal static class Extensions
    {
        public static LndError2 AsLNDError(this SwaggerException swagger)
        {
            LndError2 error;
            if (swagger is SwaggerException<RuntimeError> typedEx) {
                error = new LndError2();
                error.Error = typedEx.Result.Error;
                error.Code = typedEx.Result.Code.GetValueOrDefault();
            }
            else
            {
                error = JsonConvert.DeserializeObject<LndError2>(swagger.Response);
            }
            if(error.Error == null)
                return null;
            return error;
        }
    }
}
