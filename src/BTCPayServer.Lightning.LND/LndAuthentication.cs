using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Lightning.LND
{
    public abstract class LndAuthentication
    {
        public class FixedMacaroonAuthentication : LndAuthentication
        {
            public FixedMacaroonAuthentication(byte[] macaroon)
            {
                if (macaroon == null)
                    throw new ArgumentNullException(nameof(macaroon));
                Macaroon = macaroon;
            }
            public byte[] Macaroon { get; set; }
            public override void AddAuthentication(HttpRequestMessage httpRequest)
            {
                httpRequest.Headers.Add("Grpc-Metadata-macaroon", Encoders.Hex.EncodeData(Macaroon));
            }
        }
        public class NullAuthentication : LndAuthentication
        {
            public static NullAuthentication Instance { get; } = new NullAuthentication();

            private NullAuthentication()
            {

            }
            public override void AddAuthentication(HttpRequestMessage httpRequest)
            {
            }
        }

        public class MacaroonFileAuthentication : LndAuthentication
        {
            public MacaroonFileAuthentication(string filePath)
            {
                if (filePath == null)
                    throw new ArgumentNullException(nameof(filePath));
                FilePath = filePath;
            }
            public string FilePath { get; set; }
            public override void AddAuthentication(HttpRequestMessage httpRequest)
            {
                var bytes = File.ReadAllBytes(FilePath);
                if (!MacaroonFormat.IsValid(bytes))
                    throw new InvalidDataException($"The macaroon file '{FilePath}' is not a valid binary macaroon");
                httpRequest.Headers.Add("Grpc-Metadata-macaroon", Encoders.Hex.EncodeData(bytes));
            }
        }

        public abstract void AddAuthentication(HttpRequestMessage httpRequest);
    }
}
