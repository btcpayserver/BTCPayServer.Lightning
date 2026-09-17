using System;
using System.IO;
using System.Text;
using BTCPayServer.Lightning.Phoenixd;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class PhoenixdConnectionStringTests
    {
        [Fact]
        public void PasswordFileIsReadForEachAuthentication()
        {
            var passwordFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pwd");
            try
            {
                File.WriteAllText(passwordFile, " first-password\r\n");
                var handler = new PhoenixdConnectionStringHandler();
                var client = Assert.IsType<PhoenixdLightningClient>(handler.Create(
                    $"type=phoenixd;server=http://localhost:9740;passwordfilepath={passwordFile}",
                    Network.RegTest, out var error));

                Assert.Null(error);
                Assert.Contains($"passwordfilepath={passwordFile}", client.ToString());
                Assert.DoesNotContain("first-password", client.ToString());

                var credentials = new PhoenixdCredentials.ByPasswordFile(passwordFile);
                Assert.Equal(":first-password", Decode(credentials.CreateAuthenticationHeaderValue(null).Parameter));
                File.WriteAllText(passwordFile, "second-password");
                Assert.Equal(":second-password", Decode(credentials.CreateAuthenticationHeaderValue(null).Parameter));
            }
            finally
            {
                File.Delete(passwordFile);
            }
        }

        [Fact]
        public void PasswordAndPasswordFileAreMutuallyExclusive()
        {
            var handler = new PhoenixdConnectionStringHandler();

            var client = handler.Create(
                "type=phoenixd;server=http://localhost:9740;password=secret;passwordfilepath=/tmp/secret.pwd",
                Network.RegTest, out var error);

            Assert.Null(client);
            Assert.Equal("The key 'password' is already specified", error);
        }

        [Fact]
        public void PasswordFileMustUsePwdExtension()
        {
            var handler = new PhoenixdConnectionStringHandler();

            var client = handler.Create(
                "type=phoenixd;server=http://localhost:9740;passwordfilepath=/tmp/secret.txt",
                Network.RegTest, out var error);

            Assert.Null(client);
            Assert.Equal("The key 'passwordfilepath' should point to a .pwd file", error);
            Assert.NotNull(handler.Create(
                "type=phoenixd;server=http://localhost:9740;passwordfilepath=/tmp/secret.PWD",
                Network.RegTest, out error));
            Assert.Null(error);
        }

        [Fact]
        public void MissingAndEmptyPasswordFilesFailAuthentication()
        {
            var passwordFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pwd");
            var credentials = new PhoenixdCredentials.ByPasswordFile(passwordFile);

            Assert.Throws<FileNotFoundException>(() => credentials.CreateAuthenticationHeaderValue(null));

            try
            {
                File.WriteAllText(passwordFile, " \r\n ");
                var exception = Assert.Throws<InvalidOperationException>(() => credentials.CreateAuthenticationHeaderValue(null));
                Assert.Contains("is empty", exception.Message);
            }
            finally
            {
                File.Delete(passwordFile);
            }
        }

        [Fact]
        public void DirectPasswordStillWorks()
        {
            var credentials = new PhoenixdCredentials.ByPassword("direct-password");
            Assert.Equal("alice:direct-password", Decode(credentials.CreateAuthenticationHeaderValue("alice").Parameter));
        }

        private static string Decode(string parameter)
        {
            return Encoding.Default.GetString(Convert.FromBase64String(parameter));
        }
    }
}
