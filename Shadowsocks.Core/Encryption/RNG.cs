using System;
using System.Security.Cryptography;

namespace Shadowsocks.Encryption
{
    public static class RNG
    {
        public static void Init()
        {
        }

        public static void Close()
        {
        }

        public static void Reload()
        {
        }

        public static void GetBytes(byte[] buf)
        {
            GetBytes(buf, buf.Length);
        }

        public static void GetBytes(byte[] buf, int len)
        {
            RandomNumberGenerator.Fill(buf.AsSpan(0, len));
        }
    }
}
