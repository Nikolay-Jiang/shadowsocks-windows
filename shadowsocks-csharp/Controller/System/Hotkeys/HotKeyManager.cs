using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Shadowsocks.Controller.Hotkeys
{
    /// <summary>
    /// Minimal global hotkey support built on RegisterHotKey.  Replaces the
    /// old GlobalHotKey NuGet package, which only ships a .NET Framework
    /// assembly and cannot be relied on under .NET 10.
    /// </summary>
    public sealed class HotKey : IEquatable<HotKey>
    {
        public Keys Key { get; }
        public Keys Modifiers { get; }

        public HotKey(Keys key, Keys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public bool Equals(HotKey other)
        {
            return other != null && Key == other.Key && Modifiers == other.Modifiers;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as HotKey);
        }

        public override int GetHashCode()
        {
            return ((int)Key * 397) ^ (int)Modifiers;
        }

        public override string ToString()
        {
            return HotKeys.HotKey2Str(this);
        }
    }

    public sealed class KeyPressedEventArgs : EventArgs
    {
        public HotKey HotKey { get; }

        public KeyPressedEventArgs(HotKey hotKey)
        {
            HotKey = hotKey;
        }
    }

    public sealed class HotKeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        private sealed class HotKeyForm : Form
        {
            private readonly HotKeyManager _owner;

            public HotKeyForm(HotKeyManager owner)
            {
                _owner = owner;
                ShowInTaskbar = false;
                ShowIcon = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                Size = new Size(1, 1);
                Opacity = 0;
                MinimizeBox = false;
                MaximizeBox = false;
                Text = "ShadowsocksHotKeyWindow";
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    _owner.OnHotKey(m.WParam.ToInt32());
                    return;
                }
                base.WndProc(ref m);
            }
        }

        private readonly HotKeyForm _window;
        private readonly Dictionary<int, HotKey> _idToKey = new Dictionary<int, HotKey>();
        private readonly Dictionary<HotKey, int> _keyToId = new Dictionary<HotKey, int>();
        private int _nextId = 1;
        private bool _disposed;

        public event EventHandler<KeyPressedEventArgs> KeyPressed;

        public HotKeyManager()
        {
            _window = new HotKeyForm(this);
            // Force the window handle to be created without showing the form.
            // RegisterHotKey posts WM_HOTKEY to this hidden window.
            _ = _window.Handle;
        }

        public void Register(HotKey hotKey)
        {
            if (hotKey == null)
            {
                throw new ArgumentNullException(nameof(hotKey));
            }
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HotKeyManager));
            }
            if (_keyToId.ContainsKey(hotKey))
            {
                throw new ArgumentException("The hotkey is already registered.", nameof(hotKey));
            }

            int id = _nextId++;
            uint modifiers = ToWin32Modifiers(hotKey.Modifiers) | MOD_NOREPEAT;
            uint virtualKey = (uint)(hotKey.Key & Keys.KeyCode);
            if (!RegisterHotKey(_window.Handle, id, modifiers, virtualKey))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            _idToKey[id] = hotKey;
            _keyToId[hotKey] = id;
        }

        public void Unregister(HotKey hotKey)
        {
            if (hotKey == null)
            {
                throw new ArgumentNullException(nameof(hotKey));
            }
            if (_keyToId.TryGetValue(hotKey, out int id))
            {
                UnregisterHotKey(_window.Handle, id);
                _keyToId.Remove(hotKey);
                _idToKey.Remove(id);
            }
        }

        private void OnHotKey(int id)
        {
            if (_idToKey.TryGetValue(id, out HotKey hotKey))
            {
                KeyPressed?.Invoke(this, new KeyPressedEventArgs(hotKey));
            }
        }

        private static uint ToWin32Modifiers(Keys modifiers)
        {
            uint result = 0;
            if ((modifiers & Keys.Alt) != 0) result |= MOD_ALT;
            if ((modifiers & Keys.Control) != 0) result |= MOD_CONTROL;
            if ((modifiers & Keys.Shift) != 0) result |= MOD_SHIFT;
            return result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (int id in _idToKey.Keys)
            {
                UnregisterHotKey(_window.Handle, id);
            }
            _idToKey.Clear();
            _keyToId.Clear();
            _window.Dispose();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
