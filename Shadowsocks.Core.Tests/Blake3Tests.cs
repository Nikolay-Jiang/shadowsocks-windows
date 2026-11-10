using System;
using System.Text;
using Shadowsocks.Encryption;
using Xunit;

namespace Shadowsocks.Core.Tests
{
    public class Blake3Tests
    {
        private static string Hex(byte[] data)
        {
            return Convert.ToHexString(data).ToLowerInvariant();
        }

        [Fact]
        public void EmptyHashMatchesReference()
        {
            Assert.Equal("af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262",
                Hex(Blake3.Hash(Array.Empty<byte>())));
        }

        [Fact]
        public void AbcHashMatchesReference()
        {
            Assert.Equal("6437b3ac38465133ffb63b75273a8db548c558465d79db03fd359c6cd5bd9d85",
                Hex(Blake3.Hash(Encoding.ASCII.GetBytes("abc"))));
        }

        [Fact]
        public void QuickBrownFoxMatchesReference()
        {
            Assert.Equal("2f1514181aadccd913abd94cfa592701a5686ab23f8df1dff1b74710febc6d4a",
                Hex(Blake3.Hash(Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog"))));
        }

        [Theory]
        [InlineData(1024, "5a1c9e5d85d9898297037e8e24f69bb0e604a84c91c3b3ef4784a374812900d9")]
        [InlineData(2048, "11654ac17d073b0905429320fee0a34776cb5f10a9767287c70b627fc4f45539")]
        public void MultiChunkHashMatchesReference(int length, string expected)
        {
            byte[] input = new byte[length];
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = (byte)'a';
            }
            Assert.Equal(expected, Hex(Blake3.Hash(input)));
        }

        [Fact]
        public void DeriveKeyMatchesReference()
        {
            byte[] material = new byte[32];
            Assert.Equal("550d0146b248c42396e4d281af31e812894d076e4eac28072c9c41220ee06a4d",
                Hex(Blake3.DeriveKey("shadowsocks 2022 session subkey", material)));

            byte[] material64 = new byte[64];
            Assert.Equal("9003225aaedfad3071636bc1d95aeb39482cc501a1b68e062c142f072be60537",
                Hex(Blake3.DeriveKey("shadowsocks 2022 session subkey", material64)));

            Assert.Equal("d56afac0f10defd24359bb9a27f2d947aec7a8645d183b51bf8e3a073c5d4911",
                Hex(Blake3.DeriveKey("shadowsocks 2022 identity subkey", material)));
        }
    }
}
