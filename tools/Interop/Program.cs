using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using System.Threading;
using Shadowsocks.Encryption;

namespace Interop
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string server = "127.0.0.1";
            int port = 8388;
            string method = "2022-blake3-aes-256-gcm";
            string password = "";
            string mode = "tcp";
            string target = "127.0.0.1";
            int targetPort = 0;
            int size = 43;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--server": server = args[++i]; break;
                    case "--port": port = int.Parse(args[++i]); break;
                    case "--method": method = args[++i]; break;
                    case "--password": password = args[++i]; break;
                    case "--mode": mode = args[++i]; break;
                    case "--target": target = args[++i]; break;
                    case "--target-port": targetPort = int.Parse(args[++i]); break;
                    case "--size": size = int.Parse(args[++i]); break;
                }
            }

            Console.WriteLine($"method={method} mode={mode} server={server}:{port} target={target}:{targetPort}");
            try
            {
                if (mode == "tcp")
                {
                    RunTcp(server, port, method, password, target, targetPort, size);
                }
                else
                {
                    RunUdp(server, port, method, password, target, targetPort);
                }
                Console.WriteLine("OK");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL: " + e);
                return 1;
            }
        }

        private static void RunTcp(string server, int port, string method, string password, string target, int targetPort, int size)
        {
            byte[] payload = new byte[size];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)('a' + i % 26);

            using var listener = new TcpListener(IPAddress.Loopback, targetPort);
            listener.Start();
            var accept = listener.BeginAcceptTcpClient(null, null);
            var echoThread = new Thread(() =>
            {
                using var client = listener.EndAcceptTcpClient(accept);
                var stream = client.GetStream();
                var buffer = new byte[4096];
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, n);
                }
            });
            echoThread.IsBackground = true;
            echoThread.Start();

            using var ss = new TcpClient();
            ss.Connect(server, port);
            using var ssStream = ss.GetStream();
            ssStream.ReadTimeout = 5000;

            using IEncryptor encryptor = EncryptorFactory.GetEncryptor(method, password);
            byte[] addr = Address(target, targetPort);
            encryptor.AddrBufLength = addr.Length;

            byte[] wire = new byte[65536];
            int first = Math.Min(payload.Length, 2048);
            byte[] plain = new byte[addr.Length + first];
            Buffer.BlockCopy(addr, 0, plain, 0, addr.Length);
            Buffer.BlockCopy(payload, 0, plain, addr.Length, first);
            encryptor.Encrypt(plain, plain.Length, wire, out int wireLen);
            ssStream.Write(wire, 0, wireLen);
            for (int offset = first; offset < payload.Length; offset += 2048)
            {
                int chunk = Math.Min(2048, payload.Length - offset);
                encryptor.Encrypt(payload.AsSpan(offset, chunk).ToArray(), chunk, wire, out wireLen);
                ssStream.Write(wire, 0, wireLen);
            }

            byte[] recv = new byte[2048];
            byte[] decoded = new byte[payload.Length + 4096];
            byte[] tmp = new byte[65536];
            int decodedLen = 0;
            while (decodedLen < payload.Length)
            {
                int n = ssStream.Read(recv, 0, recv.Length);
                if (n <= 0) break;
                encryptor.Decrypt(recv, n, tmp, out int partLen);
                Buffer.BlockCopy(tmp, 0, decoded, decodedLen, partLen);
                decodedLen += partLen;
            }
            if (decodedLen != payload.Length || !decoded.AsSpan(0, decodedLen).SequenceEqual(payload))
            {
                throw new Exception($"TCP payload mismatch: got {decodedLen}, expected {payload.Length}");
            }

            ssStream.Close();
            listener.Stop();
            Console.WriteLine($"TCP echo ok ({payload.Length} bytes)");
        }

        private static void RunUdp(string server, int port, string method, string password, string target, int targetPort)
        {
            byte[] payload = Encoding.ASCII.GetBytes("udp-2022-" + Guid.NewGuid().ToString("N"));
            const int rounds = 5;

            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, targetPort));
            var echoThread = new Thread(() =>
            {
                for (int i = 0; i < rounds; i++)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = listener.Receive(ref remote);
                    listener.Send(data, data.Length, remote);
                }
            });
            echoThread.IsBackground = true;
            echoThread.Start();

            using var ss = new UdpClient();
            ss.Client.ReceiveTimeout = 5000;
            ss.Connect(server, port);

            using IEncryptor encryptor = EncryptorFactory.GetEncryptor(method, password);
            byte[] addr = Address(target, targetPort);
            for (int round = 0; round < rounds; round++)
            {
                byte[] roundPayload = payload.Concat(Encoding.ASCII.GetBytes("-" + round)).ToArray();
                byte[] plain = new byte[addr.Length + roundPayload.Length];
                Buffer.BlockCopy(addr, 0, plain, 0, addr.Length);
                Buffer.BlockCopy(roundPayload, 0, plain, addr.Length, roundPayload.Length);

                byte[] wire = new byte[65536];
                encryptor.EncryptUDP(plain, plain.Length, wire, out int wireLen);
                ss.Send(wire, wireLen);

                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] response = ss.Receive(ref from);
                byte[] decoded = new byte[65536];
                encryptor.DecryptUDP(response, response.Length, decoded, out int decodedLen);
                int addressLength = decoded[0] == 0x01 ? 7 : decoded[0] == 0x04 ? 19 : 4 + decoded[1];
                string text = Encoding.ASCII.GetString(decoded, addressLength, decodedLen - addressLength);
                if (text != Encoding.ASCII.GetString(roundPayload))
                {
                    throw new Exception($"UDP payload mismatch: '{text}'");
                }
            }
            listener.Close();
            Console.WriteLine($"UDP echo ok ({rounds} packets)");
        }

        private static byte[] Address(string host, int port)
        {
            IPAddress ip = IPAddress.Parse(host);
            byte[] b = ip.GetAddressBytes();
            if (b.Length == 4)
            {
                return new byte[] { 0x01, b[0], b[1], b[2], b[3], (byte)(port >> 8), (byte)port };
            }
            byte[] result = new byte[1 + 16 + 2];
            result[0] = 0x04;
            Buffer.BlockCopy(b, 0, result, 1, 16);
            result[17] = (byte)(port >> 8);
            result[18] = (byte)port;
            return result;
        }
    }
}
