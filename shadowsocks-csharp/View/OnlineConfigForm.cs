using Shadowsocks.Controller;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Shadowsocks.View
{
    public sealed class OnlineConfigForm : Form
    {
        private readonly ShadowsocksController _controller;
        private readonly ListBox _sources;
        private readonly TextBox _url;
        private readonly Button _update;
        private readonly Button _updateAll;
        private readonly Button _copyLink;
        private readonly Button _remove;
        private readonly Button _add;
        private readonly Button _close;

        public OnlineConfigForm(ShadowsocksController controller)
        {
            _controller = controller;
            Text = I18N.GetString("Online Config...");
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(520, 360);
            ClientSize = new Size(560, 400);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(10, 10, 10, 10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _sources = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _sources.SelectedIndexChanged += (s, e) => UpdateButtonState();
            root.Controls.Add(_sources, 0, 0);
            root.SetColumnSpan(_sources, 2);

            var side = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                Dock = DockStyle.Top,
                WrapContents = false
            };
            _update = AddButton(side, "Update", Update_Click);
            _updateAll = AddButton(side, "Update All", UpdateAll_Click);
            _copyLink = AddButton(side, "Copy Link", CopyLink_Click);
            _remove = AddButton(side, "Remove", Remove_Click);
            root.Controls.Add(side, 2, 0);

            var urlRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            urlRow.Controls.Add(new Label { Text = "URL", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _url = new TextBox { Dock = DockStyle.Fill };
            urlRow.Controls.Add(_url, 1, 0);
            _add = new Button { Text = I18N.GetString("Add"), AutoSize = true };
            _add.Click += Add_Click;
            urlRow.Controls.Add(_add, 2, 0);
            root.Controls.Add(urlRow, 0, 1);
            root.SetColumnSpan(urlRow, 2);

            _close = new Button { Text = I18N.GetString("Close"), AutoSize = true, DialogResult = DialogResult.OK };
            _close.Click += (s, e) => Close();
            root.Controls.Add(_close, 2, 1);

            Controls.Add(root);
            AcceptButton = _add;
            CancelButton = _close;

            LoadSources();
        }

        private Button AddButton(Control parent, string text, EventHandler handler)
        {
            var button = new Button
            {
                Text = I18N.GetString(text),
                AutoSize = true,
                MinimumSize = new Size(90, 28),
                Margin = new Padding(4)
            };
            button.Click += handler;
            parent.Controls.Add(button);
            return button;
        }

        private void LoadSources()
        {
            _sources.Items.Clear();
            var config = _controller.GetCurrentConfiguration();
            foreach (string source in config.onlineConfigSource ?? new List<string>())
            {
                _sources.Items.Add(source);
            }
            if (_sources.Items.Count > 0)
            {
                _sources.SelectedIndex = 0;
            }
            UpdateButtonState();
        }

        private string SelectedSource => _sources.SelectedItem as string;

        private void UpdateButtonState()
        {
            bool selected = !string.IsNullOrWhiteSpace(SelectedSource);
            _update.Enabled = selected;
            _copyLink.Enabled = selected;
            _remove.Enabled = selected;
        }

        private void Add_Click(object sender, EventArgs e)
        {
            string url = _url.Text.Trim();
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute) ||
                (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                 !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(I18N.GetString("Invalid URL"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _url.Focus();
                return;
            }
            if (_sources.Items.Contains(url))
            {
                _sources.SelectedItem = url;
                return;
            }
            _sources.Items.Add(url);
            _sources.SelectedItem = url;
            _controller.SaveOnlineConfigSource(_sources.Items.Cast<string>().ToList());
            _url.Text = "";
        }

        private void Remove_Click(object sender, EventArgs e)
        {
            string url = SelectedSource;
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }
            _sources.Items.Remove(url);
            _controller.RemoveOnlineConfig(url);
            UpdateButtonState();
        }

        private void CopyLink_Click(object sender, EventArgs e)
        {
            string url = SelectedSource;
            if (!string.IsNullOrWhiteSpace(url))
            {
                Clipboard.SetText(url);
            }
        }

        private async void Update_Click(object sender, EventArgs e)
        {
            string url = SelectedSource;
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }
            SetBusy(true);
            try
            {
                bool ok = await _controller.UpdateOnlineConfig(url);
                MessageBox.Show(ok
                        ? I18N.GetString("Online config updated successfully.")
                        : I18N.GetString("Failed to update online config."),
                    Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ErrorReporter.Report(ex, "OnlineConfig.Update");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void UpdateAll_Click(object sender, EventArgs e)
        {
            SetBusy(true);
            try
            {
                List<string> failed = await _controller.UpdateAllOnlineConfig();
                if (failed.Count == 0)
                {
                    MessageBox.Show(I18N.GetString("All online configs updated successfully."),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(I18N.GetString("Some online configs failed to update:") +
                                    Environment.NewLine + string.Join(Environment.NewLine, failed),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                ErrorReporter.Report(ex, "OnlineConfig.UpdateAll");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _update.Enabled = !busy && !string.IsNullOrWhiteSpace(SelectedSource);
            _updateAll.Enabled = !busy;
            _add.Enabled = !busy;
            _remove.Enabled = !busy && !string.IsNullOrWhiteSpace(SelectedSource);
            UseWaitCursor = busy;
        }
    }
}
