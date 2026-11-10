using Shadowsocks.Controller;
using Shadowsocks.Model;
using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Shadowsocks.View
{
    public sealed class HotkeysForm : Form
    {
        private readonly ShadowsocksController _controller;
        private readonly TextBox[] _hotkeyBoxes = new TextBox[6];
        private readonly Label[] _statusLabels = new Label[6];
        private readonly CheckBox _registerAtStartup;

        private static readonly string[] Labels =
        {
            "ToggleSystemProxy",
            "ToggleProxyMode",
            "AllowClientsFromLAN",
            "OpenLogsWindow",
            "SwitchToPreviousServer",
            "SwitchToNextServer"
        };

        private static readonly string[] CallbackNames =
        {
            "SwitchSystemProxyCallback",
            "SwitchSystemProxyModeCallback",
            "SwitchAllowLanCallback",
            "ShowLogsCallback",
            "ServerMoveUpCallback",
            "ServerMoveDownCallback"
        };

        public HotkeysForm(ShadowsocksController controller)
        {
            _controller = controller;
            Text = I18N.GetString("Edit Hotkeys...");
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(460, 330);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 8,
                Padding = new Padding(10),
                AutoSize = true
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            for (int i = 0; i < 6; i++)
            {
                var label = new Label
                {
                    Text = I18N.GetString(Labels[i]),
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(4, 7, 4, 4)
                };
                var box = new TextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    Margin = new Padding(4, 4, 4, 4)
                };
                int index = i;
                box.KeyDown += (s, e) => RecordKeyDown(index, e);
                box.KeyUp += (s, e) => FinishOnKeyUp(index, e);
                var status = new Label
                {
                    Text = "✔",
                    AutoSize = true,
                    ForeColor = Color.DarkGreen,
                    Margin = new Padding(4, 7, 4, 4)
                };

                _hotkeyBoxes[i] = box;
                _statusLabels[i] = status;
                root.Controls.Add(label, 0, i);
                root.Controls.Add(box, 1, i);
                root.Controls.Add(status, 2, i);
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            _registerAtStartup = new CheckBox
            {
                Text = I18N.GetString("RegisterHotkeysAtStartup"),
                AutoSize = true,
                Margin = new Padding(4, 10, 4, 4)
            };
            root.Controls.Add(_registerAtStartup, 0, 6);
            root.SetColumnSpan(_registerAtStartup, 3);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0)
            };
            var save = new Button { Text = I18N.GetString("Save"), AutoSize = true };
            var cancel = new Button { Text = I18N.GetString("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
            var registerAll = new Button { Text = I18N.GetString("Register All"), AutoSize = true };
            save.Click += (s, e) => RegisterAllAndUpdateStatus(true);
            cancel.Click += (s, e) => Close();
            registerAll.Click += (s, e) => RegisterAllAndUpdateStatus(false);
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(registerAll);
            root.Controls.Add(buttons, 0, 7);
            root.SetColumnSpan(buttons, 3);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Controls.Add(root);
            AcceptButton = save;
            CancelButton = cancel;

            LoadFromConfig();
        }

        private void LoadFromConfig()
        {
            HotkeyConfig config = _controller.GetCurrentConfiguration().hotkey ?? new HotkeyConfig();
            _hotkeyBoxes[0].Text = config.SwitchSystemProxy ?? "";
            _hotkeyBoxes[1].Text = config.SwitchSystemProxyMode ?? "";
            _hotkeyBoxes[2].Text = config.SwitchAllowLan ?? "";
            _hotkeyBoxes[3].Text = config.ShowLogs ?? "";
            _hotkeyBoxes[4].Text = config.ServerMoveUp ?? "";
            _hotkeyBoxes[5].Text = config.ServerMoveDown ?? "";
            _registerAtStartup.Checked = config.RegHotkeysAtStartup;
        }

        private static bool IsModifierKey(Keys key)
        {
            switch (key)
            {
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                case Keys.LWin:
                case Keys.RWin:
                    return true;
                default:
                    return false;
            }
        }

        private void RecordKeyDown(int index, KeyEventArgs e)
        {
            var builder = new StringBuilder();
            if ((e.Modifiers & Keys.Control) != 0) builder.Append("Ctrl+");
            if ((e.Modifiers & Keys.Alt) != 0) builder.Append("Alt+");
            if ((e.Modifiers & Keys.Shift) != 0) builder.Append("Shift+");

            if (builder.Length > 0 && !IsModifierKey(e.KeyCode))
            {
                var converter = new KeysConverter();
                builder.Append(converter.ConvertToString(e.KeyCode & Keys.KeyCode) ?? (e.KeyCode & Keys.KeyCode).ToString());
            }

            _hotkeyBoxes[index].Text = builder.ToString();
        }

        private void FinishOnKeyUp(int index, KeyEventArgs e)
        {
            if (_hotkeyBoxes[index].Text.EndsWith("+"))
            {
                _hotkeyBoxes[index].Text = "";
            }
        }

        private void RegisterAllAndUpdateStatus(bool save)
        {
            bool allOk = true;
            for (int i = 0; i < _hotkeyBoxes.Length; i++)
            {
                bool ok = HotkeyReg.RegHotkeyFromString(_hotkeyBoxes[i].Text, CallbackNames[i]);
                _statusLabels[i].Text = ok ? "✔" : "❌";
                _statusLabels[i].ForeColor = ok ? Color.DarkGreen : Color.Firebrick;
                allOk &= ok;
            }

            if (allOk && save)
            {
                _controller.SaveHotkeyConfig(new HotkeyConfig
                {
                    SwitchSystemProxy = _hotkeyBoxes[0].Text,
                    SwitchSystemProxyMode = _hotkeyBoxes[1].Text,
                    SwitchAllowLan = _hotkeyBoxes[2].Text,
                    ShowLogs = _hotkeyBoxes[3].Text,
                    ServerMoveUp = _hotkeyBoxes[4].Text,
                    ServerMoveDown = _hotkeyBoxes[5].Text,
                    RegHotkeysAtStartup = _registerAtStartup.Checked
                });
                Close();
            }
        }
    }
}
