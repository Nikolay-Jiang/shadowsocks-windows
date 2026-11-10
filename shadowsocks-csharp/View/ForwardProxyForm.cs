using Shadowsocks.Controller;
using Shadowsocks.Model;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Shadowsocks.View
{
    public sealed class ForwardProxyForm : Form
    {
        private readonly ShadowsocksController _controller;
        private readonly RadioButton _noProxy;
        private readonly RadioButton _socks5;
        private readonly RadioButton _http;
        private readonly TextBox _address;
        private readonly TextBox _port;
        private readonly TextBox _timeout;
        private readonly TextBox _username;
        private readonly TextBox _password;

        public ForwardProxyForm(ShadowsocksController controller)
        {
            _controller = controller;
            Text = I18N.GetString("Forward Proxy...");
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 14,
                Padding = new Padding(10),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            AddSection(root, ref row, "Type");
            _noProxy = AddRadio(root, ref row, "No Proxy", true);
            _socks5 = AddRadio(root, ref row, "SOCKS5", false);
            _http = AddRadio(root, ref row, "HTTP", false);

            AddSection(root, ref row, "Details");
            _address = AddTextRow(root, ref row, "Address");
            _port = AddTextRow(root, ref row, "Port");
            _timeout = AddTextRow(root, ref row, "Timeout");

            AddSection(root, ref row, "Credentials (optional)");
            _username = AddTextRow(root, ref row, "Username");
            _password = AddTextRow(root, ref row, "Password");

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0)
            };
            var save = new Button { Text = I18N.GetString("Save"), AutoSize = true, DialogResult = DialogResult.None };
            var cancel = new Button { Text = I18N.GetString("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += Save_Click;
            cancel.Click += (s, e) => Close();
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, row);
            root.SetColumnSpan(buttons, 2);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Controls.Add(root);
            AcceptButton = save;
            CancelButton = cancel;
            ClientSize = new Size(400, 470);

            _noProxy.CheckedChanged += (s, e) => UpdateEnabledState();
            _socks5.CheckedChanged += (s, e) => UpdateEnabledState();
            _http.CheckedChanged += (s, e) => UpdateEnabledState();

            LoadFromConfig();
            UpdateEnabledState();
        }

        private static void AddSection(TableLayoutPanel panel, ref int row, string text)
        {
            var label = new Label
            {
                Text = I18N.GetString(text),
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 4)
            };
            panel.Controls.Add(label, 0, row);
            panel.SetColumnSpan(label, 2);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row++;
        }

        private static RadioButton AddRadio(TableLayoutPanel panel, ref int row, string text, bool @checked)
        {
            var radio = new RadioButton
            {
                Text = I18N.GetString(text),
                AutoSize = true,
                Checked = @checked,
                Margin = new Padding(8, 2, 4, 2)
            };
            panel.Controls.Add(radio, 0, row);
            panel.SetColumnSpan(radio, 2);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row++;
            return radio;
        }

        private static TextBox AddTextRow(TableLayoutPanel panel, ref int row, string labelText)
        {
            var label = new Label
            {
                Text = I18N.GetString(labelText),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(8, 5, 4, 5)
            };
            var textBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2) };
            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(textBox, 1, row);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row++;
            return textBox;
        }

        private void LoadFromConfig()
        {
            ForwardProxyConfig config = _controller.GetCurrentConfiguration().proxy ?? new ForwardProxyConfig();
            _noProxy.Checked = !config.useProxy;
            _socks5.Checked = config.useProxy && config.proxyType == ForwardProxyConfig.PROXY_SOCKS5;
            _http.Checked = config.useProxy && config.proxyType == ForwardProxyConfig.PROXY_HTTP;
            _address.Text = config.proxyServer ?? "";
            _port.Text = config.proxyPort.ToString();
            _timeout.Text = config.proxyTimeout.ToString();
            _username.Text = config.authUser ?? "";
            _password.Text = config.authPwd ?? "";
        }

        private void UpdateEnabledState()
        {
            bool details = !_noProxy.Checked;
            _address.Enabled = details;
            _port.Enabled = details;
            _timeout.Enabled = details;
            bool credentials = _http.Checked;
            _username.Enabled = credentials;
            _password.Enabled = credentials;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            var config = new ForwardProxyConfig();
            if (_noProxy.Checked)
            {
                config.useProxy = false;
                config.proxyType = ForwardProxyConfig.PROXY_SOCKS5;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_address.Text))
                {
                    MessageBox.Show(I18N.GetString("Address can't be empty."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _address.Focus();
                    return;
                }
                if (!int.TryParse(_port.Text, out int port) || port <= 0 || port > 65535)
                {
                    MessageBox.Show(I18N.GetString("Port is out of range."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _port.Focus();
                    return;
                }
                if (!int.TryParse(_timeout.Text, out int timeout) || timeout <= 0 || timeout > ForwardProxyConfig.MaxProxyTimeoutSec)
                {
                    MessageBox.Show(I18N.GetString("Timeout is out of range."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _timeout.Focus();
                    return;
                }
                if (string.IsNullOrWhiteSpace(_username.Text) != string.IsNullOrWhiteSpace(_password.Text))
                {
                    MessageBox.Show(I18N.GetString("You must provide both username and password."), Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                config.useProxy = true;
                config.proxyType = _http.Checked ? ForwardProxyConfig.PROXY_HTTP : ForwardProxyConfig.PROXY_SOCKS5;
                config.proxyServer = _address.Text.Trim();
                config.proxyPort = port;
                config.proxyTimeout = timeout;
                config.authUser = _username.Text;
                config.authPwd = _password.Text;
                config.useAuth = !string.IsNullOrWhiteSpace(_username.Text);
            }

            _controller.SaveProxy(config);
            Close();
        }
    }
}
