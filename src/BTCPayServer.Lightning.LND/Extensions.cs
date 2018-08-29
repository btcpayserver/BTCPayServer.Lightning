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
            var error = JsonConvert.DeserializeObject<LndError2>(swagger.Response);
            if(error.Error == null)
                return null;
            return error;
        }
    }
}
