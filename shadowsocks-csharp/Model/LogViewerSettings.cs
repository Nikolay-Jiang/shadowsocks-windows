using System;
using System.IO;
using Newtonsoft.Json;

namespace Shadowsocks.Model
{
    /// <summary>
    /// Small replacement for the former ApplicationSettingsBase-backed
    /// log viewer state.  .NET (Core) no longer ships the old
    /// app.config/user.config settings stack, so keep the values in a
    /// plain JSON file next to gui-config.json.
    /// </summary>
    [Serializable]
    internal sealed class LogViewerSettingsData
    {
        public int Width { get; set; } = 600;
        public int Height { get; set; } = 400;
        public bool Maximized { get; set; } = true;
        public int Top { get; set; }
        public int Left { get; set; }
    }

    internal static class LogViewerSettings
    {
        private const string FileName = "gui-logviewer.json";
        private static readonly object Sync = new object();
        private static LogViewerSettingsData _data;

        public static LogViewerSettingsData Current
        {
            get
            {
                lock (Sync)
                {
                    if (_data == null)
                    {
                        _data = Load();
                    }
                    return _data;
                }
            }
        }

        private static LogViewerSettingsData Load()
        {
            try
            {
                if (File.Exists(FileName))
                {
                    return JsonConvert.DeserializeObject<LogViewerSettingsData>(File.ReadAllText(FileName))
                           ?? new LogViewerSettingsData();
                }
            }
            catch (Exception)
            {
                // A broken settings file must never prevent the log window
                // from opening.
            }
            return new LogViewerSettingsData();
        }

        public static void Save()
        {
            lock (Sync)
            {
                try
                {
                    File.WriteAllText(FileName, JsonConvert.SerializeObject(_data ?? new LogViewerSettingsData(), Formatting.Indented));
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
