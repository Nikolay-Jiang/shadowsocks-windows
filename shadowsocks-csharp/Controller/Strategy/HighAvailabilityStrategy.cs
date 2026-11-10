using NLog;
using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Shadowsocks.Controller.Strategy
{
    /// <summary>
    /// High Availability strategy.
    ///
    /// Only servers with <see cref="Server.highAvailability"/> enabled take
    /// part. The strategy is deliberately passive: a real user connection is
    /// the health probe. It keeps the current server while it is healthy,
    /// switches immediately after a failure, and only rebalances to a
    /// significantly faster server once a minute.
    /// </summary>
    class HighAvailabilityStrategy : IStrategy
    {
        public const string StrategyID = "com.shadowsocks.strategy.ha";

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private static readonly TimeSpan RebalanceInterval = TimeSpan.FromSeconds(60);
        private const double UnknownLatencyMs = 200.0;
        private const double LatencyAlpha = 0.3;

        private sealed class ServerState
        {
            public Server server;
            public double latencyMs = double.NaN;
            public DateTime lastLatencyAt = DateTime.MinValue;
            public int consecutiveFailures;
            public DateTime cooldownUntil = DateTime.MinValue;
            public DateTime lastSuccessAt = DateTime.MinValue;
            public DateTime lastFailureAt = DateTime.MinValue;
            public DateTime lastRead = DateTime.Now;
            public DateTime lastWrite = DateTime.Now;

            public ServerState(Server server)
            {
                this.server = server;
            }

            public bool InCooldown(DateTime now)
            {
                return now < cooldownUntil;
            }
        }

        private readonly Func<IEnumerable<Server>> _serverProvider;
        private readonly object _lock = new object();
        private readonly Dictionary<Server, ServerState> _states = new Dictionary<Server, ServerState>();
        private ServerState _current;
        private DateTime _lastRebalance = DateTime.MinValue;

        public HighAvailabilityStrategy(ShadowsocksController controller)
            : this(() => controller.GetCurrentConfiguration().configs)
        {
        }

        internal HighAvailabilityStrategy(Func<IEnumerable<Server>> serverProvider)
        {
            _serverProvider = serverProvider;
        }

        public string Name => I18N.GetString("High Availability");

        public string ID => StrategyID;

        public void ReloadServers()
        {
            lock (_lock)
            {
                var eligible = _serverProvider()
                    .Where(server => server.highAvailability)
                    .ToList();

                var next = new Dictionary<Server, ServerState>();
                foreach (Server server in eligible)
                {
                    if (_states.TryGetValue(server, out ServerState state))
                    {
                        state.server = server;
                        next[server] = state;
                    }
                    else
                    {
                        next[server] = new ServerState(server);
                    }
                }

                _states.Clear();
                foreach (var pair in next)
                {
                    _states[pair.Key] = pair.Value;
                }

                if (_current != null && !_states.ContainsKey(_current.server))
                {
                    _current = null;
                    _lastRebalance = DateTime.MinValue;
                }

                if (_current == null)
                {
                    ChooseNewServerLocked(DateTime.UtcNow);
                }
            }
        }

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint, EndPoint destEndPoint)
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                EnsureCurrentServerLocked(now, type == IStrategyCallerType.TCP);
                return _current?.server;
            }
        }

        private void EnsureCurrentServerLocked(DateTime now, bool allowRebalance)
        {
            if (_current == null || _current.InCooldown(now))
            {
                ChooseNewServerLocked(now);
                return;
            }

            if (!allowRebalance || now - _lastRebalance < RebalanceInterval)
            {
                return;
            }

            _lastRebalance = now;
            ServerState best = SelectBestLocked(now);
            if (best != null && best != _current &&
                ScoreLocked(best, now) < ScoreLocked(_current, now) * 0.75)
            {
                SetCurrentLocked(best, "lower latency");
            }
        }

        private void ChooseNewServerLocked(DateTime now)
        {
            ServerState best = SelectBestLocked(now);
            if (best == null)
            {
                _current = null;
                return;
            }
            if (best != _current)
            {
                SetCurrentLocked(best, _current == null ? "initial" : "failover");
            }
        }

        private ServerState SelectBestLocked(DateTime now)
        {
            if (_states.Count == 0)
            {
                return null;
            }

            var healthy = _states.Values.Where(state => !state.InCooldown(now)).ToList();
            if (healthy.Count == 0)
            {
                // Every server is cooling down. Let the one that recovers
                // first act as the next probe instead of having no server.
                return _states.Values.OrderBy(state => state.cooldownUntil).First();
            }

            ServerState best = null;
            double bestScore = double.MaxValue;
            foreach (ServerState state in healthy)
            {
                double score = ScoreLocked(state, now);
                if (score < bestScore)
                {
                    best = state;
                    bestScore = score;
                }
            }
            return best;
        }

        private double ScoreLocked(ServerState state, DateTime now)
        {
            double latency = double.IsNaN(state.latencyMs) ? UnknownLatencyMs : state.latencyMs;
            latency = Math.Min(5000, Math.Max(1, latency));

            double ageSeconds = Math.Max(0, (now - state.lastLatencyAt).TotalSeconds);
            double staleFactor = 1 + Math.Min(1, ageSeconds / 300);
            double failurePenalty = state.consecutiveFailures * 2000;
            double jitter = 0.9 + Random.Shared.NextDouble() * 0.2;

            return (latency * staleFactor + failurePenalty) * jitter;
        }

        private void SetCurrentLocked(ServerState state, string reason)
        {
            ServerState old = _current;
            _current = state;
            _lastRebalance = DateTime.UtcNow;
            if (old == state)
            {
                return;
            }
            logger.Info($"HA selected {state.server} ({reason})");
        }

        public void ChooseNewServer()
        {
            lock (_lock)
            {
                ChooseNewServerLocked(DateTime.UtcNow);
            }
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            lock (_lock)
            {
                if (!_states.TryGetValue(server, out ServerState state))
                {
                    return;
                }

                double milliseconds = Math.Min(5000, Math.Max(1, latency.TotalMilliseconds));
                state.latencyMs = double.IsNaN(state.latencyMs)
                    ? milliseconds
                    : LatencyAlpha * milliseconds + (1 - LatencyAlpha) * state.latencyMs;
                state.lastLatencyAt = DateTime.UtcNow;
                state.consecutiveFailures = 0;
                state.cooldownUntil = DateTime.MinValue;
                state.lastSuccessAt = DateTime.UtcNow;
            }
        }

        public void UpdateLastRead(Server server)
        {
            lock (_lock)
            {
                if (_states.TryGetValue(server, out ServerState state))
                {
                    state.lastRead = DateTime.UtcNow;
                }
            }
        }

        public void UpdateLastWrite(Server server)
        {
            lock (_lock)
            {
                if (_states.TryGetValue(server, out ServerState state))
                {
                    state.lastWrite = DateTime.UtcNow;
                }
            }
        }

        public void SetFailure(Server server)
        {
            lock (_lock)
            {
                if (!_states.TryGetValue(server, out ServerState state))
                {
                    return;
                }

                state.consecutiveFailures++;
                state.lastFailureAt = DateTime.UtcNow;
                state.cooldownUntil = DateTime.UtcNow + Backoff(state.consecutiveFailures);

                if (_current == state)
                {
                    _current = null;
                    _lastRebalance = DateTime.MinValue;
                }

                logger.Info($"HA failure on {server}; cooldown until {state.cooldownUntil:HH:mm:ss}");
            }
        }

        private static TimeSpan Backoff(int failures)
        {
            double seconds = Math.Min(300, 15 * Math.Pow(2, Math.Max(0, failures - 1)));
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
