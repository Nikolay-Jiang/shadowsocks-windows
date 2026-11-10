using Shadowsocks.Controller;
using Shadowsocks.Model;
using System;
using System.Drawing;
using System.Windows.Forms;
using ZXing.QrCode.Internal;

namespace Shadowsocks.View
{
    public sealed class ServerSharingForm : Form
    {
        private readonly ShadowsocksController _controller;
        private readonly ListBox _servers;
        private readonly PictureBox _qrCode;
        private readonly TextBox _url;
        private readonly Button _copy;

        public ServerSharingForm(ShadowsocksController controller)
        {
            _controller = controller;
            Text = I18N.GetString("Share Server Config...");
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(660, 400);
            ClientSize = new Size(660, 420);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _servers = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _servers.SelectedIndexChanged += (s, e) => UpdateSelectedServer();
            root.Controls.Add(_servers, 0, 0);

            _qrCode = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            root.Controls.Add(_qrCode, 1, 0);

            _url = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            root.Controls.Add(_url, 0, 1);
            root.SetColumnSpan(_url, 2);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            var close = new Button { Text = I18N.GetString("Close"), AutoSize = true, DialogResult = DialogResult.OK };
            _copy = new Button { Text = I18N.GetString("Copy Link"), AutoSize = true };
            close.Click += (s, e) => Close();
            _copy.Click += Copy_Click;
            buttons.Controls.Add(close);
            buttons.Controls.Add(_copy);
            root.Controls.Add(buttons, 0, 2);
            root.SetColumnSpan(buttons, 2);

            Controls.Add(root);
            AcceptButton = close;
            CancelButton = close;

            LoadServers();
        }

        private void LoadServers()
        {
            _servers.Items.Clear();
            var config = _controller.GetCurrentConfiguration();
            foreach (Server server in config.configs)
            {
                _servers.Items.Add(server);
            }
            if (_servers.Items.Count > 0)
            {
                _servers.SelectedIndex = 0;
            }
            else
            {
                _copy.Enabled = false;
                _url.Text = "";
            }
        }

        private void UpdateSelectedServer()
        {
            var server = _servers.SelectedItem as Server;
            if (server == null)
            {
                _copy.Enabled = false;
                _url.Text = "";
                _qrCode.Image = null;
                return;
            }

            var config = _controller.GetCurrentConfiguration();
            string url = server.GetURL(config.generateLegacyUrl);
            _url.Text = url;
            _copy.Enabled = true;

            Image old = _qrCode.Image;
            try
            {
                _qrCode.Image = CreateQrCode(url);
            }
            catch (Exception ex)
            {
                _qrCode.Image = null;
                ErrorReporter.Report(ex, "ServerSharing.QRCode");
            }
            old?.Dispose();
        }

        private static Bitmap CreateQrCode(string text)
        {
            var code = ZXing.QrCode.Internal.Encoder.encode(text, ErrorCorrectionLevel.L);
            var matrix = code.Matrix;
            int blockSize = Math.Max(4, 600 / Math.Max(1, Math.Max(matrix.Width, matrix.Height)));
            var bitmap = new Bitmap(matrix.Width * blockSize, matrix.Height * blockSize);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.Black))
            {
                graphics.Clear(Color.White);
                for (int row = 0; row < matrix.Height; row++)
                {
                    for (int column = 0; column < matrix.Width; column++)
                    {
                        if (matrix[column, row] != 0)
                        {
                            graphics.FillRectangle(brush, column * blockSize, row * blockSize, blockSize, blockSize);
                        }
                    }
                }
            }
            return bitmap;
        }

        private void Copy_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_url.Text))
            {
                Clipboard.SetText(_url.Text);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _qrCode.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
