using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;

namespace BTCPayServer.Lightning.Phoenixd;

public abstract record PhoenixdCredentials
{
    public AuthenticationHeaderValue CreateAuthenticationHeaderValue(string username)
    {
        var credentials = $"{username ?? string.Empty}:{GetPassword()}";
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.Default.GetBytes(credentials)));
    }

    protected abstract string GetPassword();

    public sealed record ByPassword : PhoenixdCredentials
    {
        public ByPassword(string password)
        {
            Password = password;
        }

        public string Password { get; set; }

        protected override string GetPassword() => Password;
    }

    public sealed record ByPasswordFile : PhoenixdCredentials
    {
        public ByPasswordFile(string passwordFilePath)
        {
            PasswordFilePath = passwordFilePath;
        }

        public string PasswordFilePath { get; set; }
        protected override string GetPassword()
        {
            var password = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(PasswordFilePath))
                .TrimStart('\uFEFF').Trim();
            if (password.Length == 0)
                throw new InvalidOperationException($"The password file '{PasswordFilePath}' is empty");
            foreach (var character in password)
            {
                if (char.IsControl(character))
                    throw new InvalidDataException($"The password file '{PasswordFilePath}' must contain a single-line password without control characters");
            }
            return password;
        }
    }
}
