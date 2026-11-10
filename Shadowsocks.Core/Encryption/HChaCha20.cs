using System;
using System.Buffers.Binary;

namespace Shadowsocks.Encryption
{
    /// <summary>
    /// HChaCha20 key derivation (RFC 8439 style), used to implement
    /// XChaCha20-Poly1305 on top of the platform's ChaCha20-Poly1305.
    /// </summary>
    internal static class HChaCha20
    {
        private static readonly uint[] Sigma =
        {
            0x61707865, 0x3320646e, 0x79622d32, 0x6b206574
        };

        public static byte[] DeriveSubkey(byte[] key, ReadOnlySpan<byte> nonce)
        {
            if (key.Length != 32)
            {
                throw new ArgumentException("HChaCha20 requires a 32-byte key", nameof(key));
            }
            if (nonce.Length != 16)
            {
                throw new ArgumentException("HChaCha20 requires a 16-byte nonce", nameof(nonce));
            }

            uint[] state = new uint[16];
            Sigma.CopyTo(state, 0);
            for (int i = 0; i < 8; i++)
            {
                state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(i * 4, 4));
            }
            for (int i = 0; i < 4; i++)
            {
                state[12 + i] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(i * 4, 4));
            }

            for (int i = 0; i < 10; i++)
            {
                QuarterRound(state, 0, 4, 8, 12);
                QuarterRound(state, 1, 5, 9, 13);
                QuarterRound(state, 2, 6, 10, 14);
                QuarterRound(state, 3, 7, 11, 15);
                QuarterRound(state, 0, 5, 10, 15);
                QuarterRound(state, 1, 6, 11, 12);
                QuarterRound(state, 2, 7, 8, 13);
                QuarterRound(state, 3, 4, 9, 14);
            }

            byte[] result = new byte[32];
            for (int i = 0; i < 4; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4, 4), state[i]);
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16 + i * 4, 4), state[12 + i]);
            }
            return result;
        }

        private static void QuarterRound(uint[] s, int a, int b, int c, int d)
        {
            s[a] += s[b];
            s[d] = RotateLeft(s[d] ^ s[a], 16);
            s[c] += s[d];
            s[b] = RotateLeft(s[b] ^ s[c], 12);
            s[a] += s[b];
            s[d] = RotateLeft(s[d] ^ s[a], 8);
            s[c] += s[d];
            s[b] = RotateLeft(s[b] ^ s[c], 7);
        }

        private static uint RotateLeft(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }
    }
}
