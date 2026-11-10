using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using Shadowsocks.Encryption.Exception;

namespace Shadowsocks.Encryption.AEAD
{
    /// <summary>
    /// AEAD methods implemented with the cryptography shipped in .NET.
    /// Replaces the former libsscrypto P/Invoke backends (mbed TLS, OpenSSL and
    /// libsodium), which also removes the x86 native dependency.
    /// </summary>
    public sealed class AEADManagedEncryptor
        : AEADEncryptor
    {
        private const int CIPHER_AES_GCM = 1;
        private const int CIPHER_CHACHA20_POLY1305 = 2;
        private const int CIPHER_XCHACHA20_POLY1305 = 3;

        private static readonly Dictionary<string, EncryptorInfo> _ciphers = new Dictionary<string, EncryptorInfo>
        {
            { "aes-128-gcm", new EncryptorInfo("aes-128-gcm", 16, 16, 12, 16, CIPHER_AES_GCM) },
            { "aes-192-gcm", new EncryptorInfo("aes-192-gcm", 24, 24, 12, 16, CIPHER_AES_GCM) },
            { "aes-256-gcm", new EncryptorInfo("aes-256-gcm", 32, 32, 12, 16, CIPHER_AES_GCM) },
            { "chacha20-ietf-poly1305", new EncryptorInfo("chacha20-ietf-poly1305", 32, 32, 12, 16, CIPHER_CHACHA20_POLY1305) },
            { "xchacha20-ietf-poly1305", new EncryptorInfo("xchacha20-ietf-poly1305", 32, 32, 24, 16, CIPHER_XCHACHA20_POLY1305) },
        };

        private AesGcm _encAes;
        private AesGcm _decAes;
        private ChaCha20Poly1305 _encChaCha;
        private ChaCha20Poly1305 _decChaCha;
        private byte[] _encSubkey;
        private byte[] _decSubkey;

        public AEADManagedEncryptor(string method, string password)
            : base(method, password)
        {
        }

        public static List<string> SupportedCiphers()
        {
            return new List<string>(_ciphers.Keys);
        }

        protected override Dictionary<string, EncryptorInfo> getCiphers()
        {
            return _ciphers;
        }

        public override void InitCipher(byte[] salt, bool isEncrypt, bool isUdp)
        {
            base.InitCipher(salt, isEncrypt, isUdp);

            if (isEncrypt)
            {
                _encSubkey = DeriveSessionKey(_encryptSalt, _Masterkey, keyLen);
                DisposeEncryptCipher();
                CreateCipher(_encSubkey, true);
            }
            else
            {
                _decSubkey = DeriveSessionKey(_decryptSalt, _Masterkey, keyLen);
                DisposeDecryptCipher();
                CreateCipher(_decSubkey, false);
            }
        }

        private void CreateCipher(byte[] subkey, bool encrypt)
        {
            switch (_cipher)
            {
                case CIPHER_AES_GCM:
                    if (encrypt)
                    {
                        _encAes = new AesGcm(subkey, tagLen);
                    }
                    else
                    {
                        _decAes = new AesGcm(subkey, tagLen);
                    }
                    break;
                case CIPHER_CHACHA20_POLY1305:
                    if (encrypt)
                    {
                        _encChaCha = new ChaCha20Poly1305(subkey);
                    }
                    else
                    {
                        _decChaCha = new ChaCha20Poly1305(subkey);
                    }
                    break;
                case CIPHER_XCHACHA20_POLY1305:
                    // The XChaCha subkey depends on the per-message nonce;
                    // it is derived in cipherEncrypt/cipherDecrypt.
                    break;
                default:
                    throw new CryptoErrorException("unsupported cipher");
            }
        }

        public override void cipherEncrypt(byte[] plaintext, uint plen, byte[] ciphertext, ref uint clen)
        {
            int length = checked((int)plen);
            switch (_cipher)
            {
                case CIPHER_AES_GCM:
                    _encAes.Encrypt(_encNonce, plaintext.AsSpan(0, length), ciphertext.AsSpan(0, length),
                        ciphertext.AsSpan(length, tagLen));
                    break;
                case CIPHER_CHACHA20_POLY1305:
                    _encChaCha.Encrypt(_encNonce, plaintext.AsSpan(0, length), ciphertext.AsSpan(0, length),
                        ciphertext.AsSpan(length, tagLen));
                    break;
                case CIPHER_XCHACHA20_POLY1305:
                    EncryptXChaCha(_encSubkey, _encNonce, plaintext, length, ciphertext);
                    break;
                default:
                    throw new CryptoErrorException("unsupported cipher");
            }
            clen = (uint)(length + tagLen);
        }

        public override void cipherDecrypt(byte[] ciphertext, uint clenIn, byte[] plaintext, ref uint plen)
        {
            int length = checked((int)clenIn) - tagLen;
            if (length < 0)
            {
                throw new CryptoErrorException("ciphertext too short");
            }
            try
            {
                switch (_cipher)
                {
                    case CIPHER_AES_GCM:
                        _decAes.Decrypt(_decNonce, ciphertext.AsSpan(0, length),
                            ciphertext.AsSpan(length, tagLen), plaintext.AsSpan(0, length));
                        break;
                    case CIPHER_CHACHA20_POLY1305:
                        _decChaCha.Decrypt(_decNonce, ciphertext.AsSpan(0, length),
                            ciphertext.AsSpan(length, tagLen), plaintext.AsSpan(0, length));
                        break;
                    case CIPHER_XCHACHA20_POLY1305:
                        DecryptXChaCha(_decSubkey, _decNonce, ciphertext, length, plaintext);
                        break;
                    default:
                        throw new CryptoErrorException("unsupported cipher");
                }
            }
            catch (CryptographicException e)
            {
                throw new CryptoErrorException("authentication failed", e);
            }
            plen = (uint)length;
        }

        private void EncryptXChaCha(byte[] sessionKey, byte[] nonce, byte[] plaintext, int length, byte[] ciphertext)
        {
            byte[] subkey = HChaCha20.DeriveSubkey(sessionKey, nonce.AsSpan(0, 16));
            Span<byte> ietfNonce = stackalloc byte[12];
            ietfNonce.Clear();
            // 4 zero bytes followed by the last 8 bytes of the 24-byte nonce.
            nonce.AsSpan(16, 8).CopyTo(ietfNonce.Slice(4));
            using (var chacha = new ChaCha20Poly1305(subkey))
            {
                chacha.Encrypt(ietfNonce, plaintext.AsSpan(0, length), ciphertext.AsSpan(0, length),
                    ciphertext.AsSpan(length, tagLen));
            }
        }

        private void DecryptXChaCha(byte[] sessionKey, byte[] nonce, byte[] ciphertext, int length, byte[] plaintext)
        {
            byte[] subkey = HChaCha20.DeriveSubkey(sessionKey, nonce.AsSpan(0, 16));
            Span<byte> ietfNonce = stackalloc byte[12];
            ietfNonce.Clear();
            nonce.AsSpan(16, 8).CopyTo(ietfNonce.Slice(4));
            using (var chacha = new ChaCha20Poly1305(subkey))
            {
                chacha.Decrypt(ietfNonce, ciphertext.AsSpan(0, length), ciphertext.AsSpan(length, tagLen),
                    plaintext.AsSpan(0, length));
            }
        }

        private void DisposeEncryptCipher()
        {
            _encAes?.Dispose();
            _encAes = null;
            _encChaCha?.Dispose();
            _encChaCha = null;
        }

        private void DisposeDecryptCipher()
        {
            _decAes?.Dispose();
            _decAes = null;
            _decChaCha?.Dispose();
            _decChaCha = null;
        }

        public override void Dispose()
        {
            DisposeEncryptCipher();
            DisposeDecryptCipher();
            if (_encSubkey != null)
            {
                CryptographicOperations.ZeroMemory(_encSubkey);
                _encSubkey = null;
            }
            if (_decSubkey != null)
            {
                CryptographicOperations.ZeroMemory(_decSubkey);
                _decSubkey = null;
            }
            if (_Masterkey != null)
            {
                CryptographicOperations.ZeroMemory(_Masterkey);
                _Masterkey = null;
            }
        }
    }
}
