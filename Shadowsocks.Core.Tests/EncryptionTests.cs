using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Shadowsocks.Encryption;
using Shadowsocks.Encryption.Exception;
using Xunit;

namespace Shadowsocks.Core.Tests
{
    public class EncryptionTests
    {
        private static readonly byte[] Addr = { 0x01, 1, 2, 3, 4, 0x00, 0x50 };
        private static readonly byte[] Payload = Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\n\r\n");

        public static IEnumerable<object[]> ClassicMethods()
        {
            yield return new object[] { "aes-128-gcm", "password" };
            yield return new object[] { "aes-192-gcm", "password" };
            yield return new object[] { "aes-256-gcm", "password" };
            yield return new object[] { "chacha20-ietf-poly1305", "password" };
            yield return new object[] { "xchacha20-ietf-poly1305", "password" };
            yield return new object[] { "plain", "password" };
        }

        [Theory]
        [MemberData(nameof(ClassicMethods))]
        public void TcpRoundTrip(string method, string password)
        {
            using IEncryptor client = EncryptorFactory.GetEncryptor(method, password);
            using IEncryptor server = EncryptorFactory.GetEncryptor(method, password);
            client.AddrBufLength = Addr.Length;
            server.AddrBufLength = Addr.Length;

            byte[] plain = new byte[Addr.Length + Payload.Length];
            Buffer.BlockCopy(Addr, 0, plain, 0, Addr.Length);
            Buffer.BlockCopy(Payload, 0, plain, Addr.Length, Payload.Length);

            byte[] cipher = new byte[65536];
            client.Encrypt(plain, plain.Length, cipher, out int cipherLen);
            Assert.True(cipherLen >= plain.Length);

            byte[] decrypted = new byte[65536];
            server.Decrypt(cipher, cipherLen, decrypted, out int decryptedLen);
            Assert.Equal(plain.Length, decryptedLen);
            Assert.Equal(plain, decrypted.AsSpan(0, decryptedLen).ToArray());
        }

        [Theory]
        [MemberData(nameof(ClassicMethods))]
        public void UdpRoundTrip(string method, string password)
        {
            using IEncryptor client = EncryptorFactory.GetEncryptor(method, password);
            using IEncryptor server = EncryptorFactory.GetEncryptor(method, password);

            byte[] cipher = new byte[65536];
            client.EncryptUDP(Payload, Payload.Length, cipher, out int cipherLen);
            byte[] decrypted = new byte[65536];
            server.DecryptUDP(cipher, cipherLen, decrypted, out int decryptedLen);
            Assert.Equal(Payload.Length, decryptedLen);
            Assert.Equal(Payload, decrypted.AsSpan(0, decryptedLen).ToArray());
        }

        [Theory]
        [MemberData(nameof(ClassicMethods))]
        public void UdpRoundTripWithReusedEncryptor(string method, string password)
        {
            using IEncryptor client = EncryptorFactory.GetEncryptor(method, password);
            using IEncryptor server = EncryptorFactory.GetEncryptor(method, password);
            byte[] cipher = new byte[65536];
            byte[] decrypted = new byte[65536];

            for (int round = 0; round < 8; round++)
            {
                byte[] payload = Encoding.ASCII.GetBytes("udp-round-" + round);
                client.EncryptUDP(payload, payload.Length, cipher, out int cipherLen);
                server.DecryptUDP(cipher, cipherLen, decrypted, out int decryptedLen);
                Assert.Equal(payload, decrypted.AsSpan(0, decryptedLen).ToArray());

                server.EncryptUDP(payload, payload.Length, cipher, out cipherLen);
                client.DecryptUDP(cipher, cipherLen, decrypted, out decryptedLen);
                Assert.Equal(payload, decrypted.AsSpan(0, decryptedLen).ToArray());
            }
        }

        [Fact]
        public void Aead2022RejectsWrongKeyLength()
        {
            Assert.Throws<CryptoErrorException>(() =>
                EncryptorFactory.GetEncryptor("2022-blake3-aes-256-gcm", Convert.ToBase64String(new byte[16])));
            Assert.Throws<CryptoErrorException>(() =>
                EncryptorFactory.GetEncryptor("2022-blake3-aes-128-gcm", Convert.ToBase64String(new byte[32])));
        }

        [Fact]
        public void Aead2022AnonymousTcpRequestDecodesWithReferenceServer()
        {
            byte[] psk = new byte[32];
            for (int i = 0; i < psk.Length; i++) psk[i] = (byte)i;
            string password = Convert.ToBase64String(psk);

            using IEncryptor client = EncryptorFactory.GetEncryptor("2022-blake3-aes-256-gcm", password);
            client.AddrBufLength = Addr.Length;
            byte[] plain = new byte[Addr.Length + Payload.Length];
            Buffer.BlockCopy(Addr, 0, plain, 0, Addr.Length);
            Buffer.BlockCopy(Payload, 0, plain, Addr.Length, Payload.Length);

            byte[] cipher = new byte[65536];
            client.Encrypt(plain, plain.Length, cipher, out int cipherLen);

            byte[] decoded = Decode2022TcpRequest(psk, null, cipher.AsSpan(0, cipherLen).ToArray(), 32);
            Assert.Equal(plain, decoded);
        }

        [Fact]
        public void Aead2022IdentifiedTcpRequestDecodesWithReferenceServer()
        {
            byte[] basePsk = new byte[32];
            byte[] userPsk = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                basePsk[i] = (byte)(0x40 + i);
                userPsk[i] = (byte)(0x80 + i);
            }
            string password = Convert.ToBase64String(basePsk) + ":" + Convert.ToBase64String(userPsk);

            using IEncryptor client = EncryptorFactory.GetEncryptor("2022-blake3-aes-256-gcm", password);
            client.AddrBufLength = Addr.Length;
            byte[] plain = new byte[Addr.Length + Payload.Length];
            Buffer.BlockCopy(Addr, 0, plain, 0, Addr.Length);
            Buffer.BlockCopy(Payload, 0, plain, Addr.Length, Payload.Length);

            byte[] cipher = new byte[65536];
            client.Encrypt(plain, plain.Length, cipher, out int cipherLen);
            byte[] decoded = Decode2022TcpRequest(userPsk, basePsk, cipher.AsSpan(0, cipherLen).ToArray(), 32);
            Assert.Equal(plain, decoded);
        }

        [Fact]
        public void Aead2022IdentifiedTcpRequestAes128DecodesWithReferenceServer()
        {
            byte[] basePsk = new byte[16];
            byte[] userPsk = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                basePsk[i] = (byte)(0x20 + i);
                userPsk[i] = (byte)(0x60 + i);
            }
            string password = Convert.ToBase64String(basePsk) + ":" + Convert.ToBase64String(userPsk);

            using IEncryptor client = EncryptorFactory.GetEncryptor("2022-blake3-aes-128-gcm", password);
            client.AddrBufLength = Addr.Length;
            byte[] plain = new byte[Addr.Length + Payload.Length];
            Buffer.BlockCopy(Addr, 0, plain, 0, Addr.Length);
            Buffer.BlockCopy(Payload, 0, plain, Addr.Length, Payload.Length);

            byte[] cipher = new byte[65536];
            client.Encrypt(plain, plain.Length, cipher, out int cipherLen);
            byte[] decoded = Decode2022TcpRequest(userPsk, basePsk, cipher.AsSpan(0, cipherLen).ToArray(), 16);
            Assert.Equal(plain, decoded);
        }

        [Fact]
        public void Aead2022AnonymousUdpPacketDecodesWithReferenceServer()
        {
            byte[] psk = new byte[32];
            for (int i = 0; i < psk.Length; i++) psk[i] = (byte)i;
            using IEncryptor client = EncryptorFactory.GetEncryptor("2022-blake3-aes-256-gcm",
                Convert.ToBase64String(psk));

            byte[] cipher = new byte[65536];
            client.EncryptUDP(Payload, Payload.Length, cipher, out int cipherLen);
            byte[] decoded = Decode2022UdpPacket(psk, null, cipher.AsSpan(0, cipherLen).ToArray(), 32);
            Assert.Equal(Payload, decoded);
        }

        private static byte[] Decode2022TcpRequest(byte[] trafficKey, byte[] identityKey, byte[] wire, int keyLen)
        {
            int offset = 0;
            byte[] salt = wire.AsSpan(offset, keyLen).ToArray();
            offset += keyLen;

            if (identityKey != null)
            {
                byte[] block = wire.AsSpan(offset, 16).ToArray();
                offset += 16;
                byte[] material = new byte[identityKey.Length + salt.Length];
                Buffer.BlockCopy(identityKey, 0, material, 0, identityKey.Length);
                Buffer.BlockCopy(salt, 0, material, identityKey.Length, salt.Length);
                byte[] subkey = Blake3.DeriveKey("shadowsocks 2022 identity subkey", material);
                byte[] hash = Blake3.Hash(trafficKey);
                byte[] expected = AesEcb(subkey.AsSpan(0, identityKey.Length).ToArray(),
                    hash.AsSpan(0, 16).ToArray(), true);
                Assert.Equal(expected, block);
            }

            byte[] bodyKey = DeriveSubkey(trafficKey, salt);
            using var gcm = new AesGcm(bodyKey, 16);
            byte[] nonce = new byte[12];

            byte[] fixedPlain = Open(gcm, nonce, wire, ref offset, 11);
            Increment(nonce);
            Assert.Equal(0, fixedPlain[0]);
            int bodyLen = BinaryPrimitives.ReadUInt16BigEndian(fixedPlain.AsSpan(9, 2));
            byte[] body = Open(gcm, nonce, wire, ref offset, bodyLen);
            Increment(nonce);

            int addrLen = AddressLength(body);
            int pad = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(addrLen, 2));
            Assert.InRange(pad, 0, 900);
            int payloadOffset = addrLen + 2 + pad;
            byte[] result = new byte[addrLen + body.Length - payloadOffset];
            Buffer.BlockCopy(body, 0, result, 0, addrLen);
            Buffer.BlockCopy(body, payloadOffset, result, addrLen, body.Length - payloadOffset);
            return result;
        }

        private static byte[] Decode2022UdpPacket(byte[] trafficKey, byte[] identityKey, byte[] wire, int keyLen)
        {
            byte[] sep = AesEcb(identityKey ?? trafficKey, wire.AsSpan(0, 16).ToArray(), false);
            int offset = 16;
            if (identityKey != null)
            {
                byte[] block = wire.AsSpan(offset, 16).ToArray();
                offset += 16;
                byte[] hash = Blake3.Hash(trafficKey);
                for (int i = 0; i < 16; i++) hash[i] ^= sep[i];
                byte[] expected = AesEcb(identityKey, hash.AsSpan(0, 16).ToArray(), true);
                Assert.Equal(expected, block);
            }

            byte[] sessionId = sep.AsSpan(0, 8).ToArray();
            byte[] subkey = DeriveSubkey(trafficKey, sessionId);
            using var gcm = new AesGcm(subkey, 16);
            byte[] body = new byte[wire.Length - offset - 16];
            gcm.Decrypt(sep.AsSpan(4, 12), wire.AsSpan(offset, body.Length), wire.AsSpan(wire.Length - 16, 16), body);

            Assert.Equal(0, body[0]);
            int pad = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(9, 2));
            int dataOffset = 11 + pad;
            byte[] result = new byte[body.Length - dataOffset];
            Buffer.BlockCopy(body, dataOffset, result, 0, result.Length);
            return result;
        }

        private static byte[] DeriveSubkey(byte[] key, byte[] salt)
        {
            byte[] material = new byte[key.Length + salt.Length];
            Buffer.BlockCopy(key, 0, material, 0, key.Length);
            Buffer.BlockCopy(salt, 0, material, key.Length, salt.Length);
            return Blake3.DeriveKey("shadowsocks 2022 session subkey", material).AsSpan(0, key.Length).ToArray();
        }

        private static byte[] Open(AesGcm gcm, byte[] nonce, byte[] wire, ref int offset, int length)
        {
            byte[] plain = new byte[length];
            gcm.Decrypt(nonce, wire.AsSpan(offset, length), wire.AsSpan(offset + length, 16), plain);
            offset += length + 16;
            return plain;
        }

        private static void Increment(byte[] nonce)
        {
            for (int i = 0; i < nonce.Length; i++)
            {
                if (++nonce[i] != 0) break;
            }
        }

        private static byte[] AesEcb(byte[] key, byte[] input, bool encrypt)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            using var transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
            return transform.TransformFinalBlock(input, 0, 16);
        }

        private static int AddressLength(byte[] body)
        {
            return body[0] switch
            {
                0x01 => 1 + 4 + 2,
                0x04 => 1 + 16 + 2,
                0x03 => 1 + 1 + body[1] + 2,
                _ => throw new InvalidOperationException("bad address type"),
            };
        }
    }
}
