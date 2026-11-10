using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Shadowsocks.Encryption.AEAD;
using Shadowsocks.Encryption.Stream;

namespace Shadowsocks.Encryption
{
    public static class EncryptorFactory
    {
        public const string DefaultMethod = "chacha20-ietf-poly1305";

        private static readonly Dictionary<string, Type> _registeredEncryptors = new Dictionary<string, Type>();

        private static readonly Type[] ConstructorTypes = { typeof(string), typeof(string) };

        static EncryptorFactory()
        {
            foreach (string method in AEADManagedEncryptor.SupportedCiphers())
            {
                _registeredEncryptors[method] = typeof(AEADManagedEncryptor);
            }

            foreach (string method in Aead2022Encryptor.SupportedCiphers())
            {
                _registeredEncryptors[method] = typeof(Aead2022Encryptor);
            }

            foreach (string method in PlainEncryptor.SupportedCiphers())
            {
                _registeredEncryptors[method] = typeof(PlainEncryptor);
            }
        }

        public static IEncryptor GetEncryptor(string method, string password)
        {
            if (string.IsNullOrEmpty(method))
            {
                method = DefaultMethod;
            }

            method = method.ToLowerInvariant();
            if (!_registeredEncryptors.TryGetValue(method, out Type t))
            {
                throw new ArgumentException($"Unsupported encryption method: {method}", nameof(method));
            }

            ConstructorInfo c = t.GetConstructor(ConstructorTypes);
            if (c == null) throw new System.Exception("Invalid ctor");
            try
            {
                return (IEncryptor)c.Invoke(new object[] { method, password });
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        public static bool IsSupported(string method)
        {
            return !string.IsNullOrEmpty(method) && _registeredEncryptors.ContainsKey(method.ToLowerInvariant());
        }

        public static List<string> SupportedMethods()
        {
            return new List<string>(_registeredEncryptors.Keys);
        }

        public static string DumpRegisteredEncryptor()
        {
            var sb = new StringBuilder();
            sb.Append(Environment.NewLine);
            sb.AppendLine("=========================");
            sb.AppendLine("Registered Encryptor Info");
            foreach (var encryptor in _registeredEncryptors)
            {
                sb.AppendLine(String.Format("{0}=>{1}", encryptor.Key, encryptor.Value.Name));
            }

            sb.AppendLine("=========================");
            return sb.ToString();
        }
    }
}
