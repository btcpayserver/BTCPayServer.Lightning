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
        [Theory]
        [InlineData("")]
        [InlineData(".txt")]
        [InlineData(".pwd")]
        public void PasswordFileIsReadForEachAuthentication(string extension)
        {
            var passwordFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
            try
            {
                File.WriteAllText(passwordFile, " first-password\r\n");
                var handler = new PhoenixdConnectionStringHandler();
                Assert.IsType<PhoenixdLightningClient>(handler.Create(
                    $"type=phoenixd;server=http://localhost:9740;passwordfilepath={passwordFile}",
                    Network.RegTest, out var error));

                Assert.Null(error);

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

        [Theory]
        [InlineData("/nonexistent/secret")]
        [InlineData("/nonexistent/secret.txt")]
        [InlineData("/nonexistent/secret.PWD")]
        public void PasswordFileIsNotReadByHandler(string path)
        {
            var handler = new PhoenixdConnectionStringHandler();

            Assert.NotNull(handler.Create(
                $"type=phoenixd;server=http://localhost:9740;passwordfilepath={path}",
                Network.RegTest, out var error));
            Assert.Null(error);
        }

        [Theory]
        [InlineData("password\nanother-password")]
        [InlineData("password\0")]
        [InlineData("pass\tword")]
        public void InvalidPasswordFileContentsFailAuthentication(string contents)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                var credentials = new PhoenixdCredentials.ByPasswordFile(path);
                File.WriteAllText(path, contents);
                Assert.Throws<InvalidDataException>(() => credentials.CreateAuthenticationHeaderValue(null));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void InvalidUtf8PasswordFileFailsAuthentication()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                File.WriteAllBytes(path, new byte[] { 0xff, 0xfe, 0x41, 0 });
                var credentials = new PhoenixdCredentials.ByPasswordFile(path);
                Assert.Throws<DecoderFallbackException>(() => credentials.CreateAuthenticationHeaderValue(null));
            }
            finally
            {
                File.Delete(path);
            }
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
