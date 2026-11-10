using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Shadowsocks.Controller.Strategy;
using Shadowsocks.Model;
using System.Collections.Generic;
using System.Net;

namespace Shadowsocks.Test
{
    [TestClass]
    public class HighAvailabilityTest
    {
        [TestMethod]
        public void ServerDefaultsToNotParticipating()
        {
            Server server = JsonConvert.DeserializeObject<Server>("{}");
            Assert.IsFalse(server.highAvailability);

            server = JsonConvert.DeserializeObject<Server>("{\"highAvailability\":true}");
            Assert.IsTrue(server.highAvailability);
        }

        [TestMethod]
        public void OnlyEnabledServersParticipate()
        {
            var enabled = new Server { server = "a.example", server_port = 8388, highAvailability = true };
            var disabled = new Server { server = "b.example", server_port = 8388, highAvailability = false };
            var strategy = new HighAvailabilityStrategy(() => new List<Server> { enabled, disabled });

            strategy.ReloadServers();
            Assert.AreSame(enabled, strategy.GetAServer(IStrategyCallerType.TCP, Endpoint(), null));

            var none = new HighAvailabilityStrategy(() => new List<Server> { disabled });
            none.ReloadServers();
            Assert.IsNull(none.GetAServer(IStrategyCallerType.TCP, Endpoint(), null));
        }

        [TestMethod]
        public void FailureMovesToAnotherEnabledServer()
        {
            var first = new Server { server = "a.example", server_port = 8388, highAvailability = true };
            var second = new Server { server = "b.example", server_port = 8388, highAvailability = true };
            var strategy = new HighAvailabilityStrategy(() => new List<Server> { first, second });
            strategy.ReloadServers();

            Server current = strategy.GetAServer(IStrategyCallerType.TCP, Endpoint(), null);
            Assert.IsNotNull(current);
            Server other = current == first ? second : first;

            strategy.SetFailure(current);
            Assert.AreSame(other, strategy.GetAServer(IStrategyCallerType.TCP, Endpoint(), null));
        }

        private static IPEndPoint Endpoint()
        {
            return new IPEndPoint(IPAddress.Loopback, 12345);
        }
    }
}
