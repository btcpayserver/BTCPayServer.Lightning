using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using BTCPayServer.Lightning.LND;
using NBitcoin;
using NBitcoin.DataEncoders;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class LndMacaroonFileTests
    {
        private const string Macaroon = "0201036c6e640247030a10b0dbbde28f009f83d330bde05075ca251201301a160a0761646472657373120472656164120577726974651a170a08696e766f6963657312047265616412057772697465000006200ae088692e67cf14e767c3d2a4a67ce489150bf810654ff980e1b7a7e263d5e8";

        [Theory]
        [InlineData("")]
        [InlineData(".bin")]
        [InlineData(".macaroon")]
        public void MacaroonFileIsValidatedAndReadForEachAuthentication(string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
            try
            {
                var client = Assert.IsType<LndClient>(new LndConnectionStringHandler().Create(
                    $"type=lnd-rest;server=http://localhost:8080/;macaroonfilepath={path}",
                    Network.RegTest, out var error));
                Assert.Null(error);
                var authentication = client.SwaggerClient._LndSettings.CreateLndAuthentication();
                var bytes = Encoders.Hex.DecodeData(Macaroon);
                File.WriteAllBytes(path, bytes);
                using var first = new HttpRequestMessage();
                authentication.AddAuthentication(first);
                Assert.Equal(Macaroon, first.Headers.GetValues("Grpc-Metadata-macaroon").Single());

                // A rotated file must be re-read and revalidated, not cached.
                bytes[bytes.Length - 1] ^= 1;
                File.WriteAllBytes(path, bytes);
                using var second = new HttpRequestMessage();
                authentication.AddAuthentication(second);
                Assert.Equal(Encoders.Hex.EncodeData(bytes), second.Headers.GetValues("Grpc-Metadata-macaroon").Single());

                File.WriteAllText(path, "not a macaroon");
                using var invalid = new HttpRequestMessage();
                Assert.Throws<InvalidDataException>(() => authentication.AddAuthentication(invalid));
                Assert.False(invalid.Headers.Contains("Grpc-Metadata-macaroon"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("02")]
        [InlineData("0202016100000620")]
        [InlineData("020201610000060100")]
        [InlineData("02ffffffffffffffffff")]
        public void InvalidMacaroonFileIsNotSent(string hex)
        {
            AssertInvalid(Encoders.Hex.DecodeData(hex));
        }

        [Fact]
        public void RejectsMalformedPacketsAndTrailingData()
        {
            var bytes = Encoders.Hex.DecodeData(Macaroon);
            AssertInvalid(bytes.Take(bytes.Length - 1).ToArray());
            AssertInvalid(bytes.Concat(new byte[] { 0 }).ToArray());
            bytes[2] = 127; // Location length exceeds the remaining input.
            AssertInvalid(bytes);
            AssertInvalid(Encoding.ASCII.GetBytes(Macaroon));
            AssertInvalid(Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoders.Hex.DecodeData(Macaroon))));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AcceptsV2MacaroonWithOptionalLocationAndThirdPartyCaveat(bool thirdParty)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                // Identifier "a", then an optional third-party caveat with identifier "c".
                var header = new byte[] { 2, 2, 1, (byte)'a', 0 };
                var caveat = thirdParty
                    ? new byte[] { 1, 1, (byte)'l', 2, 1, (byte)'c', 4, 1, (byte)'v', 0 }
                    : Array.Empty<byte>();
                var bytes = header.Concat(caveat).Concat(new byte[] { 0, 6, 32 })
                    .Concat(new byte[32]).ToArray();
                File.WriteAllBytes(path, bytes);
                using var request = new HttpRequestMessage();
                new LndAuthentication.MacaroonFileAuthentication(path).AddAuthentication(request);
                Assert.Equal(Encoders.Hex.EncodeData(bytes), request.Headers.GetValues("Grpc-Metadata-macaroon").Single());
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void AcceptsV1MacaroonFile()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                // V1 packets include a four-digit hexadecimal total length and a newline.
                var bytes = Encoding.ASCII.GetBytes("0011location lnd\n0012identifier id\n")
                    .Concat(Encoding.ASCII.GetBytes("002fsignature "))
                    .Concat(new byte[32]).Concat(new byte[] { 10 }).ToArray();
                File.WriteAllBytes(path, bytes);
                using var request = new HttpRequestMessage();
                new LndAuthentication.MacaroonFileAuthentication(path).AddAuthentication(request);
                Assert.Equal(Encoders.Hex.EncodeData(bytes), request.Headers.GetValues("Grpc-Metadata-macaroon").Single());
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MissingMacaroonFileFailsAtReadTime()
        {
            var authentication = new LndAuthentication.MacaroonFileAuthentication(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
            using var request = new HttpRequestMessage();
            Assert.Throws<FileNotFoundException>(() => authentication.AddAuthentication(request));
            Assert.False(request.Headers.Contains("Grpc-Metadata-macaroon"));
        }

        private static void AssertInvalid(byte[] bytes)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.macaroon");
            try
            {
                File.WriteAllBytes(path, bytes);
                using var request = new HttpRequestMessage();
                var authentication = new LndAuthentication.MacaroonFileAuthentication(path);
                Assert.Throws<InvalidDataException>(() => authentication.AddAuthentication(request));
                Assert.False(request.Headers.Contains("Grpc-Metadata-macaroon"));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
