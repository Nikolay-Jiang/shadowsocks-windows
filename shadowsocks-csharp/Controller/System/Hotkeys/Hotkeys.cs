using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace Shadowsocks.Controller.Hotkeys
{
    public static class HotKeys
    {
        private static HotKeyManager _hotKeyManager;

        public delegate void HotKeyCallBackHandler();

        // map key and corresponding handler function
        private static readonly Dictionary<HotKey, HotKeyCallBackHandler> _keymap =
            new Dictionary<HotKey, HotKeyCallBackHandler>();

        public static void Init(ShadowsocksController controller)
        {
            // The hidden hotkey window is created lazily, only when a hotkey
            // is actually registered.
            HotkeyCallbacks.InitInstance(controller);
        }

        private static void EnsureManager()
        {
            if (_hotKeyManager == null)
            {
                _hotKeyManager = new HotKeyManager();
                _hotKeyManager.KeyPressed += HotKeyManagerPressed;
            }
        }

        public static void Destroy()
        {
            if (_hotKeyManager == null)
            {
                return;
            }
            _hotKeyManager.KeyPressed -= HotKeyManagerPressed;
            _hotKeyManager.Dispose();
            _hotKeyManager = null;
        }

        private static void HotKeyManagerPressed(object sender, KeyPressedEventArgs e)
        {
            if (_keymap.TryGetValue(e.HotKey, out HotKeyCallBackHandler callback))
            {
                callback();
            }
        }

        public static bool RegHotkey(HotKey hotkey, HotKeyCallBackHandler callback)
        {
            UnregExistingHotkey(callback);
            EnsureManager();
            return Register(hotkey, callback);
        }

        public static bool UnregExistingHotkey(HotKeyCallBackHandler callback)
        {
            if (IsCallbackExists(callback, out HotKey existingHotKey))
            {
                Unregister(existingHotKey);
                return true;
            }
            return false;
        }

        public static bool IsHotkeyExists(HotKey hotKey)
        {
            if (hotKey == null) throw new ArgumentNullException(nameof(hotKey));
            return _keymap.Any(v => v.Key.Equals(hotKey));
        }

        public static bool IsCallbackExists(HotKeyCallBackHandler callback, out HotKey hotkey)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (_keymap.Any(v => v.Value == callback))
            {
                hotkey = _keymap.First(v => v.Value == callback).Key;
                return true;
            }
            hotkey = null;
            return false;
        }

        #region Converters

        public static string HotKey2Str(HotKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return HotKey2Str(key.Key, key.Modifiers);
        }

        public static string HotKey2Str(Keys key, Keys modifiers)
        {
            if (!Enum.IsDefined(typeof(Keys), key))
                throw new InvalidEnumArgumentException(nameof(key), (int)key, typeof(Keys));

            var parts = new List<string>();
            if ((modifiers & Keys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & Keys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & Keys.Shift) != 0) parts.Add("Shift");

            Keys keyCode = key & Keys.KeyCode;
            if (keyCode == Keys.None)
            {
                return null;
            }

            string keyName;
            int digit = keyCode - Keys.D0;
            if (digit >= 0 && digit <= 9)
            {
                // KeysConverter returns "2" for Keys.D2 on .NET, while the old
                // WPF implementation produced "D2". Keep the old format so
                // existing hotkey settings continue to round-trip.
                keyName = "D" + digit;
            }
            else
            {
                var converter = new KeysConverter();
                keyName = converter.ConvertToString(keyCode) ?? keyCode.ToString();
            }
            parts.Add(keyName);
            return string.Join("+", parts);
        }

        public static HotKey Str2HotKey(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                int offset = value.LastIndexOf("+", StringComparison.OrdinalIgnoreCase);
                if (offset <= 0) return null;

                string modifierText = value.Substring(0, offset).Trim();
                string keyText = value.Substring(offset + 1).Trim();

                Keys modifiers = Keys.None;
                foreach (string part in modifierText.Split('+'))
                {
                    switch (part.Trim().ToLowerInvariant())
                    {
                        case "ctrl":
                        case "control":
                            modifiers |= Keys.Control;
                            break;
                        case "alt":
                            modifiers |= Keys.Alt;
                            break;
                        case "shift":
                            modifiers |= Keys.Shift;
                            break;
                        default:
                            return null;
                    }
                }

                object parsed;
                if (keyText.Length == 1 && keyText[0] >= '0' && keyText[0] <= '9')
                {
                    parsed = (Keys)((int)Keys.D0 + (keyText[0] - '0'));
                }
                else
                {
                    var converter = new KeysConverter();
                    parsed = converter.ConvertFromString(keyText);
                }
                if (parsed == null)
                {
                    return null;
                }

                return new HotKey((Keys)parsed & Keys.KeyCode, modifiers);
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        #endregion

        private static bool Register(HotKey key, HotKeyCallBackHandler callback)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            try
            {
                _hotKeyManager.Register(key);
                _keymap[key] = callback;
                return true;
            }
            catch (ArgumentException)
            {
                // already called this method with the specific hotkey
                // return success silently
                return true;
            }
            catch (Win32Exception)
            {
                // this hotkey already registered by other programs
                // notify user to change key
                return false;
            }
        }

        private static void Unregister(HotKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            _hotKeyManager.Unregister(key);
            _keymap.Remove(key);
        }
    }
}
