using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LND
{
    internal static class Extensions
    {
        public static LNDError AsLNDError(this SwaggerException swagger)
        {
            LNDError error;

            try
            {
                error = JsonConvert.DeserializeObject<LNDError>(swagger.Response);
            }
            catch (Exception)
            {
                var nested = JsonConvert.DeserializeObject<LNDNestedError>(swagger.Response);
                error = nested.Error;
            }
            
            error.Error = error.Message;
            return error.Error == null ? null : error;
        }
    }
}
