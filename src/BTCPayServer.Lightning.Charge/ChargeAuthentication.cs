using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace BTCPayServer.Lightning.Charge
{
    public abstract class ChargeAuthentication
    {
        public class UserPasswordAuthentication : ChargeAuthentication
        {
            public UserPasswordAuthentication(NetworkCredential networkCredential)
            {
                if (networkCredential == null)
                    throw new ArgumentNullException(nameof(networkCredential));
                NetworkCredential = networkCredential;
            }
            public NetworkCredential NetworkCredential { get; }

            public override string GetBase64Creds()
            {
                return Convert.ToBase64String(Encoding.ASCII.GetBytes($"{NetworkCredential.UserName}:{NetworkCredential.Password}"));
            }
        }

        public class CookieFileAuthentication : ChargeAuthentication
        {
            public CookieFileAuthentication(string filePath)
            {
                if (filePath == null)
                    throw new ArgumentNullException(nameof(filePath));
                FilePath = filePath;
            }
            public string FilePath { get; set; }
            public override string GetBase64Creds()
            {
                try
                {
                    var password = File.ReadAllText(FilePath);
                    return Convert.ToBase64String(Encoding.ASCII.GetBytes($"api-token:{password}"));
                }
                catch
                {
                    return Convert.ToBase64String(Encoding.ASCII.GetBytes(""));
                }
            }
        }

        public abstract string GetBase64Creds();
    }
}
