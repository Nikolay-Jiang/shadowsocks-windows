using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using Shadowsocks.Encryption.CircularBuffer;
using Shadowsocks.Encryption.Exception;

namespace Shadowsocks.Encryption
{
    /// <summary>
    /// Shadowsocks 2022 / SIP022 client codec, with optional SIP023 user
    /// identities.  The wire format follows shadowsocks-c's
    /// src/crypto/aead2022.cpp; this class is the client half only.
    ///
    /// Password format:
    ///   "BASE64_PSK"                 anonymous / single user
    ///   "BASE64_SERVER:BASE64_USER"  identified user (SIP023)
    /// </summary>
    public sealed class Aead2022Encryptor : EncryptorBase
    {
        private const int TagLen = 16;
        private const int NonceLen = 12;
        private const int SessionIdLen = 8;
        private const int IdentityLen = 16;
        private const int MaxPad = 900;
        private const int MaxChunk = 0xFFFF;
        private const int StreamChunk = 0x3FFF; // keep one chunk inside the relay buffers
        private const int TimeSkew = 30;
        private const int UdpWindowBits = 256;
        private const int UdpSepHeaderLen = 16;

        private const string SubkeyContext = "shadowsocks 2022 session subkey";
        private const string IdentityContext = "shadowsocks 2022 identity subkey";
        private const byte TypeClient = 0;
        private const byte TypeServer = 1;

        private sealed class Spec
        {
            public readonly string Name;
            public readonly int KeyLen;

            public Spec(string name, int keyLen)
            {
                Name = name;
                KeyLen = keyLen;
            }
        }

        private static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            { "2022-blake3-aes-128-gcm", new Spec("2022-blake3-aes-128-gcm", 16) },
            { "2022-blake3-aes-256-gcm", new Spec("2022-blake3-aes-256-gcm", 32) },
        };

        private readonly Spec _spec;
        private readonly byte[] _trafficKey;
        private readonly byte[] _identityKey;
        private readonly object _lock = new object();

        // TCP state
        private ByteCircularBuffer _decBuffer;
        private byte[] _encSalt;
        private byte[] _decSalt;
        private AesGcm _encGcm;
        private AesGcm _decGcm;
        private byte[] _encNonce;
        private byte[] _decNonce;
        private bool _encSaltSent;
        private bool _encHeaderSent;
        private bool _decSaltReceived;
        private bool _decHeaderReceived;
        private int _decBodyLen;

        // UDP state
        private bool _txReady;
        private byte[] _txSessionId;
        private ulong _txPacketId;
        private byte[] _txSubkey;
        private bool _rxReady;
        private bool _rxPrevReady;
        private byte[] _rxId;
        private byte[] _rxPrevId;
        private ulong _rxHighest;
        private ulong _rxPrevHighest;
        private byte[] _rxWindow;
        private byte[] _rxPrevWindow;
        private byte[] _rxSubkey;
        private byte[] _rxSubkeyId;

        public Aead2022Encryptor(string method, string password)
            : base(method, password)
        {
            method = method.ToLowerInvariant();
            if (!Specs.TryGetValue(method, out _spec))
            {
                throw new CryptoErrorException("unsupported 2022 method: " + method);
            }

            string basePsk = password ?? string.Empty;
            string userPsk = null;
            int colon = basePsk.IndexOf(':');
            if (colon >= 0)
            {
                userPsk = basePsk.Substring(colon + 1);
                basePsk = basePsk.Substring(0, colon);
            }

            byte[] baseKey = ParsePsk(basePsk, _spec.KeyLen, "server PSK");
            if (userPsk != null)
            {
                _identityKey = baseKey;
                _trafficKey = ParsePsk(userPsk, _spec.KeyLen, "user PSK");
            }
            else
            {
                _trafficKey = baseKey;
                _identityKey = null;
            }

            _decBuffer = new ByteCircularBuffer(MAX_INPUT_SIZE * 2);
            _encNonce = new byte[NonceLen];
            _decNonce = new byte[NonceLen];
            _rxWindow = new byte[UdpWindowBits / 8];
            _rxPrevWindow = new byte[UdpWindowBits / 8];
            _rxId = new byte[SessionIdLen];
            _rxPrevId = new byte[SessionIdLen];
        }

        public static List<string> SupportedCiphers()
        {
            return new List<string>(Specs.Keys);
        }

        private static byte[] ParsePsk(string text, int expectedLen, string what)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new CryptoErrorException(what + " is empty");
            }
            text = text.Trim().Replace('-', '+').Replace('_', '/');
            int padding = (4 - text.Length % 4) % 4;
            text = text.PadRight(text.Length + padding, '=');
            byte[] key;
            try
            {
                key = Convert.FromBase64String(text);
            }
            catch (FormatException e)
            {
                throw new CryptoErrorException(what + " is not valid base64", e);
            }
            if (key.Length != expectedLen)
            {
                throw new CryptoErrorException($"{what} must decode to {expectedLen} bytes (got {key.Length})");
            }
            return key;
        }

        #region TCP

        public override void Encrypt(byte[] buf, int length, byte[] outbuf, out int outlength)
        {
            lock (_lock)
            {
                int offset = 0;
                if (!_encSaltSent)
                {
                    _encSalt = RandomBytes(_spec.KeyLen);
                    byte[] subkey = DeriveSubkey(_trafficKey, _encSalt);
                    _encGcm = new AesGcm(subkey, TagLen);
                    CryptographicOperations.ZeroMemory(subkey);
                    Buffer.BlockCopy(_encSalt, 0, outbuf, offset, _encSalt.Length);
                    offset += _encSalt.Length;

                    if (_identityKey != null)
                    {
                        byte[] block = IdentityTcpBlock(_identityKey, _trafficKey, _encSalt);
                        Buffer.BlockCopy(block, 0, outbuf, offset, IdentityLen);
                        offset += IdentityLen;
                    }
                    _encSaltSent = true;
                }

                if (!_encHeaderSent)
                {
                    _encHeaderSent = true;
                    int addrLen = AddrBufLength;
                    if (addrLen < 3 || addrLen > length)
                    {
                        throw new CryptoErrorException("invalid SOCKS address length for 2022 TCP");
                    }

                    ReadOnlySpan<byte> input = buf.AsSpan(0, length);
                    ReadOnlySpan<byte> addr = input.Slice(0, addrLen);
                    ReadOnlySpan<byte> payload = input.Slice(addrLen);
                    int pad = payload.Length == 0 ? RandomNumberGenerator.GetInt32(MaxPad) + 1 : 0;
                    int firstPayload = Math.Min(payload.Length, MaxChunk - addrLen - 2 - pad);
                    int bodyLen = addrLen + 2 + pad + firstPayload;

                    byte[] fixedHeader = new byte[11];
                    fixedHeader[0] = TypeClient;
                    BinaryPrimitives.WriteUInt64BigEndian(fixedHeader.AsSpan(1, 8), UnixNow());
                    BinaryPrimitives.WriteUInt16BigEndian(fixedHeader.AsSpan(9, 2), (ushort)bodyLen);
                    Seal(fixedHeader, outbuf, ref offset);

                    byte[] body = new byte[bodyLen];
                    addr.CopyTo(body);
                    BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(addrLen, 2), (ushort)pad);
                    if (pad > 0)
                    {
                        RandomBytes(pad).CopyTo(body, addrLen + 2);
                    }
                    if (firstPayload > 0)
                    {
                        payload.Slice(0, firstPayload).CopyTo(body.AsSpan(addrLen + 2 + pad));
                    }
                    Seal(body, outbuf, ref offset);

                    SealChunks(payload.Slice(firstPayload), outbuf, ref offset);
                }
                else
                {
                    SealChunks(buf.AsSpan(0, length), outbuf, ref offset);
                }

                outlength = offset;
            }
        }

        private void SealChunks(ReadOnlySpan<byte> input, byte[] outbuf, ref int offset)
        {
            while (input.Length > 0)
            {
                int chunk = Math.Min(input.Length, StreamChunk);
                byte[] len = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)chunk);
                Seal(len, outbuf, ref offset);
                Seal(input.Slice(0, chunk), outbuf, ref offset);
                input = input.Slice(chunk);
            }
        }

        private void Seal(ReadOnlySpan<byte> plaintext, byte[] outbuf, ref int offset)
        {
            _encGcm.Encrypt(_encNonce, plaintext, outbuf.AsSpan(offset, plaintext.Length),
                outbuf.AsSpan(offset + plaintext.Length, TagLen));
            IncrementLittleEndian(_encNonce);
            offset += plaintext.Length + TagLen;
        }

        public override void Decrypt(byte[] buf, int length, byte[] outbuf, out int outlength)
        {
            lock (_lock)
            {
                _decBuffer.Put(buf, 0, length);
                outlength = 0;

                if (!_decSaltReceived)
                {
                    if (_decBuffer.Size < _spec.KeyLen)
                    {
                        return;
                    }
                    _decSalt = _decBuffer.Get(_spec.KeyLen);
                    byte[] subkey = DeriveSubkey(_trafficKey, _decSalt);
                    _decGcm = new AesGcm(subkey, TagLen);
                    CryptographicOperations.ZeroMemory(subkey);
                    _decSaltReceived = true;
                }

                while (true)
                {
                    if (!_decHeaderReceived)
                    {
                        int fixedLen = 11 + _spec.KeyLen;
                        if (_decBuffer.Size < fixedLen + TagLen)
                        {
                            return;
                        }
                        byte[] fixedCipher = _decBuffer.Peek(fixedLen + TagLen);
                        byte[] fixedPlain = new byte[fixedLen];
                        Open(fixedCipher, 0, fixedLen + TagLen, fixedPlain);
                        if (fixedPlain[0] != TypeServer ||
                            !TimestampOk(BinaryPrimitives.ReadUInt64BigEndian(fixedPlain.AsSpan(1, 8))))
                        {
                            throw new CryptoErrorException("invalid 2022 response header");
                        }
                        ReadOnlySpan<byte> echo = fixedPlain.AsSpan(9, _spec.KeyLen);
                        if (!CryptographicOperations.FixedTimeEquals(echo, _encSalt))
                        {
                            throw new CryptoErrorException("2022 response salt mismatch");
                        }
                        _decBodyLen = BinaryPrimitives.ReadUInt16BigEndian(fixedPlain.AsSpan(9 + _spec.KeyLen, 2));
                        if (_decBodyLen <= 0)
                        {
                            throw new CryptoErrorException("invalid 2022 response body length");
                        }
                        _decBuffer.Skip(fixedLen + TagLen);
                        _decHeaderReceived = true;
                    }

                    if (_decBodyLen > 0)
                    {
                        if (_decBuffer.Size < _decBodyLen + TagLen)
                        {
                            return;
                        }
                        if (_decBodyLen > BufferSize)
                        {
                            throw new CryptoErrorException("2022 chunk is larger than the relay buffer");
                        }
                        if (outlength + _decBodyLen > BufferSize)
                        {
                            return;
                        }
                        byte[] bodyCipher = _decBuffer.Get(_decBodyLen + TagLen);
                        byte[] bodyPlain = new byte[_decBodyLen];
                        Open(bodyCipher, 0, bodyCipher.Length, bodyPlain);
                        Buffer.BlockCopy(bodyPlain, 0, outbuf, outlength, _decBodyLen);
                        outlength += _decBodyLen;
                        _decBodyLen = 0;
                    }

                    if (_decBuffer.Size < 2 + TagLen)
                    {
                        return;
                    }
                    byte[] lenCipher = _decBuffer.Peek(2 + TagLen);
                    byte[] lenPlain = new byte[2];
                    Open(lenCipher, 0, lenCipher.Length, lenPlain);
                    _decBodyLen = BinaryPrimitives.ReadUInt16BigEndian(lenPlain);
                    if (_decBodyLen <= 0)
                    {
                        throw new CryptoErrorException("invalid 2022 chunk length");
                    }
                    _decBuffer.Skip(2 + TagLen);
                }
            }
        }

        private void Open(byte[] ciphertext, int offset, int lengthWithTag, byte[] plaintext)
        {
            try
            {
                _decGcm.Decrypt(_decNonce, ciphertext.AsSpan(offset, lengthWithTag - TagLen),
                    ciphertext.AsSpan(offset + lengthWithTag - TagLen, TagLen), plaintext);
            }
            catch (CryptographicException e)
            {
                throw new CryptoErrorException("2022 authentication failed", e);
            }
            IncrementLittleEndian(_decNonce);
        }

        #endregion

        #region UDP

        public override void EncryptUDP(byte[] buf, int length, byte[] outbuf, out int outlength)
        {
            lock (_lock)
            {
                if (!_txReady)
                {
                    _txSessionId = RandomBytes(SessionIdLen);
                    _txPacketId = 0;
                    _txSubkey = DeriveSubkey(_trafficKey, _txSessionId);
                    _txReady = true;
                }

                byte[] sep = new byte[UdpSepHeaderLen];
                Buffer.BlockCopy(_txSessionId, 0, sep, 0, SessionIdLen);
                BinaryPrimitives.WriteUInt64BigEndian(sep.AsSpan(SessionIdLen, 8), _txPacketId);

                byte[] body = new byte[11 + length];
                body[0] = TypeClient;
                BinaryPrimitives.WriteUInt64BigEndian(body.AsSpan(1, 8), UnixNow());
                BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(9, 2), 0);
                Buffer.BlockCopy(buf, 0, body, 11, length);

                int offset = 0;
                byte[] sepKey = _identityKey ?? _trafficKey;
                byte[] sepEncrypted = AesEcb(sepKey, sep, true);
                Buffer.BlockCopy(sepEncrypted, 0, outbuf, offset, sepEncrypted.Length);
                offset += sepEncrypted.Length;

                if (_identityKey != null)
                {
                    byte[] block = IdentityUdpBlock(_identityKey, _trafficKey, sep);
                    Buffer.BlockCopy(block, 0, outbuf, offset, IdentityLen);
                    offset += IdentityLen;
                }

                using (var gcm = new AesGcm(_txSubkey, TagLen))
                {
                    gcm.Encrypt(sep.AsSpan(4, NonceLen), body, outbuf.AsSpan(offset, body.Length),
                        outbuf.AsSpan(offset + body.Length, TagLen));
                }
                offset += body.Length + TagLen;
                _txPacketId++;
                outlength = offset;
            }
        }

        public override void DecryptUDP(byte[] buf, int length, byte[] outbuf, out int outlength)
        {
            lock (_lock)
            {
                if (length < UdpSepHeaderLen + TagLen + 11)
                {
                    throw new CryptoErrorException("2022 UDP packet too short");
                }

                byte[] sep = AesEcb(_trafficKey, buf.AsSpan(0, UdpSepHeaderLen).ToArray(), false);
                byte[] sessionId = sep.AsSpan(0, SessionIdLen).ToArray();
                ulong packetId = BinaryPrimitives.ReadUInt64BigEndian(sep.AsSpan(SessionIdLen, 8));

                if (_rxSubkeyId == null || !_rxSubkeyId.AsSpan().SequenceEqual(sessionId))
                {
                    _rxSubkey = DeriveSubkey(_trafficKey, sessionId);
                    _rxSubkeyId = sessionId;
                }

                int bodyLen = length - UdpSepHeaderLen - TagLen;
                byte[] body = new byte[bodyLen];
                try
                {
                    using (var gcm = new AesGcm(_rxSubkey, TagLen))
                    {
                        gcm.Decrypt(sep.AsSpan(4, NonceLen), buf.AsSpan(UdpSepHeaderLen, bodyLen),
                            buf.AsSpan(length - TagLen, TagLen), body);
                    }
                }
                catch (CryptographicException e)
                {
                    throw new CryptoErrorException("2022 UDP authentication failed", e);
                }

                if (bodyLen < 11 + SessionIdLen ||
                    body[0] != TypeServer ||
                    !TimestampOk(BinaryPrimitives.ReadUInt64BigEndian(body.AsSpan(1, 8))))
                {
                    throw new CryptoErrorException("invalid 2022 UDP response");
                }
                if (!body.AsSpan(9, SessionIdLen).SequenceEqual(_txSessionId))
                {
                    throw new CryptoErrorException("2022 UDP session echo mismatch");
                }

                int pad = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(9 + SessionIdLen, 2));
                int dataOffset = 9 + SessionIdLen + 2 + pad;
                if (pad > MaxPad || dataOffset > bodyLen)
                {
                    throw new CryptoErrorException("invalid 2022 UDP padding");
                }

                AcceptUdpPacket(sessionId, packetId);
                Buffer.BlockCopy(body, dataOffset, outbuf, 0, bodyLen - dataOffset);
                outlength = bodyLen - dataOffset;
            }
        }

        private void AcceptUdpPacket(byte[] sessionId, ulong packetId)
        {
            if (_rxReady && !_rxId.AsSpan().SequenceEqual(sessionId))
            {
                bool back = _rxPrevReady && _rxPrevId.AsSpan().SequenceEqual(sessionId);
                byte[] idSwap = _rxId;
                _rxId = _rxPrevId;
                _rxPrevId = idSwap;
                ulong highSwap = _rxHighest;
                _rxHighest = _rxPrevHighest;
                _rxPrevHighest = highSwap;
                byte[] windowSwap = _rxWindow;
                _rxWindow = _rxPrevWindow;
                _rxPrevWindow = windowSwap;
                bool readySwap = _rxReady;
                _rxReady = _rxPrevReady;
                _rxPrevReady = readySwap;
                if (!back)
                {
                    _rxReady = false;
                }
            }

            if (!_rxReady)
            {
                WindowReset(sessionId, packetId);
            }
            else if (!WindowCheck(packetId))
            {
                throw new CryptoErrorException("2022 UDP replayed packet");
            }
            WindowUpdate(packetId);
        }

        private void WindowReset(byte[] sessionId, ulong packetId)
        {
            Buffer.BlockCopy(sessionId, 0, _rxId, 0, SessionIdLen);
            Array.Clear(_rxWindow, 0, _rxWindow.Length);
            _rxHighest = packetId > 0 ? packetId - 1 : 0;
            _rxReady = true;
        }

        private bool WindowCheck(ulong packetId)
        {
            if (packetId > _rxHighest)
            {
                return true;
            }
            if (_rxHighest - packetId >= UdpWindowBits)
            {
                return false;
            }
            ulong bit = packetId % UdpWindowBits;
            return (_rxWindow[bit / 8] & (1 << (int)(bit % 8))) == 0;
        }

        private void WindowUpdate(ulong packetId)
        {
            if (packetId > _rxHighest)
            {
                ulong diff = packetId - _rxHighest;
                if (diff >= UdpWindowBits)
                {
                    Array.Clear(_rxWindow, 0, _rxWindow.Length);
                }
                else
                {
                    for (ulong i = _rxHighest + 1; i <= packetId; i++)
                    {
                        ulong bit = i % UdpWindowBits;
                        _rxWindow[bit / 8] &= (byte)~(1 << (int)(bit % 8));
                    }
                }
                _rxHighest = packetId;
            }
            ulong setBit = packetId % UdpWindowBits;
            _rxWindow[setBit / 8] |= (byte)(1 << (int)(setBit % 8));
        }

        #endregion

        #region 2022 helpers

        private static byte[] DeriveSubkey(byte[] key, byte[] salt)
        {
            byte[] material = new byte[key.Length + salt.Length];
            Buffer.BlockCopy(key, 0, material, 0, key.Length);
            Buffer.BlockCopy(salt, 0, material, key.Length, salt.Length);
            byte[] full = Blake3.DeriveKey(SubkeyContext, material);
            byte[] subkey = full.AsSpan(0, key.Length).ToArray();
            CryptographicOperations.ZeroMemory(material);
            CryptographicOperations.ZeroMemory(full);
            return subkey;
        }

        private static byte[] IdentityTcpBlock(byte[] identityKey, byte[] trafficKey, byte[] salt)
        {
            byte[] material = new byte[identityKey.Length + salt.Length];
            Buffer.BlockCopy(identityKey, 0, material, 0, identityKey.Length);
            Buffer.BlockCopy(salt, 0, material, identityKey.Length, salt.Length);
            byte[] subkey = Blake3.DeriveKey(IdentityContext, material);
            byte[] hash = Blake3.Hash(trafficKey);
            byte[] block = AesEcb(subkey.AsSpan(0, identityKey.Length).ToArray(),
                hash.AsSpan(0, IdentityLen).ToArray(), true);
            CryptographicOperations.ZeroMemory(material);
            CryptographicOperations.ZeroMemory(subkey);
            CryptographicOperations.ZeroMemory(hash);
            return block;
        }

        private static byte[] IdentityUdpBlock(byte[] identityKey, byte[] trafficKey, byte[] sep)
        {
            byte[] hash = Blake3.Hash(trafficKey);
            for (int i = 0; i < IdentityLen; i++)
            {
                hash[i] ^= sep[i];
            }
            byte[] block = AesEcb(identityKey, hash.AsSpan(0, IdentityLen).ToArray(), true);
            CryptographicOperations.ZeroMemory(hash);
            return block;
        }

        private static byte[] AesEcb(byte[] key, byte[] input, bool encrypt)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                using (ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor())
                {
                    return transform.TransformFinalBlock(input, 0, 16);
                }
            }
        }

        private static byte[] RandomBytes(int length)
        {
            byte[] result = new byte[length];
            RandomNumberGenerator.Fill(result);
            return result;
        }

        private static ulong UnixNow()
        {
            return (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static bool TimestampOk(ulong timestamp)
        {
            ulong now = UnixNow();
            ulong diff = now > timestamp ? now - timestamp : timestamp - now;
            return diff <= TimeSkew;
        }

        private static void IncrementLittleEndian(byte[] nonce)
        {
            for (int i = 0; i < nonce.Length; i++)
            {
                if (++nonce[i] != 0)
                {
                    break;
                }
            }
        }

        #endregion

        public override void Dispose()
        {
            lock (_lock)
            {
                _encGcm?.Dispose();
                _decGcm?.Dispose();
                _encGcm = null;
                _decGcm = null;
                if (_trafficKey != null) CryptographicOperations.ZeroMemory(_trafficKey);
                if (_identityKey != null) CryptographicOperations.ZeroMemory(_identityKey);
                if (_txSubkey != null) CryptographicOperations.ZeroMemory(_txSubkey);
                if (_rxSubkey != null) CryptographicOperations.ZeroMemory(_rxSubkey);
                if (_encSalt != null) CryptographicOperations.ZeroMemory(_encSalt);
                if (_decSalt != null) CryptographicOperations.ZeroMemory(_decSalt);
            }
        }
    }
}
