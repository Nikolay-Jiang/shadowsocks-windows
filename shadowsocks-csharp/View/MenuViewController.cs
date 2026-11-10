using NLog;
using Shadowsocks.Controller;
using Shadowsocks.Controller.Strategy;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using Shadowsocks.Util;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Shadowsocks.View
{
    public class MenuViewController
    {
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        private ShadowsocksController controller;

        private NotifyIcon _notifyIcon;
        private Icon icon, icon_in, icon_out, icon_both, previousIcon;

        private string _urlToOpen;

        private ContextMenuStrip contextMenu1;
        private ToolStripMenuItem disableItem;
        private ToolStripMenuItem AutoStartupItem;
        private ToolStripMenuItem ProtocolHandlerItem;
        private ToolStripMenuItem ShareOverLANItem;
        private ToolStripItem SeperatorItem;
        private ToolStripMenuItem ConfigItem;
        private ToolStripMenuItem ServersItem;
        private ToolStripMenuItem globalModeItem;
        private ToolStripMenuItem PACModeItem;
        private ToolStripMenuItem localPACItem;
        private ToolStripMenuItem onlinePACItem;
        private ToolStripMenuItem editLocalPACItem;
        private ToolStripMenuItem updateFromGeositeItem;
        private ToolStripMenuItem editGFWUserRuleItem;
        private ToolStripMenuItem editOnlinePACItem;
        private ToolStripMenuItem secureLocalPacUrlToggleItem;
        private ToolStripMenuItem regenerateLocalPacOnUpdateItem;
        private ToolStripMenuItem proxyItem;
        private ToolStripMenuItem hotKeyItem;
        private ToolStripMenuItem VerboseLoggingToggleItem;
        private ToolStripMenuItem ShowPluginOutputToggleItem;
        private ToolStripMenuItem onlineConfigItem;

        private ConfigForm configForm;
        private LogForm logForm;

        private ServerSharingForm serverSharingForm;
        private HotkeysForm hotkeysForm;
        private ForwardProxyForm forwardProxyForm;
        private OnlineConfigForm onlineConfigForm;

        // color definition for icon color transformation
        private readonly Color colorMaskBlue = Color.FromArgb(255, 25, 125, 191);
        private readonly Color colorMaskDarkSilver = Color.FromArgb(128, 192, 192, 192);
        private readonly Color colorMaskLightSilver = Color.FromArgb(192, 192, 192);
        private readonly Color colorMaskEclipse = Color.FromArgb(192, 64, 64, 64);

        public MenuViewController(ShadowsocksController controller)
        {
            this.controller = controller;

            LoadMenu();

            controller.EnableStatusChanged += controller_EnableStatusChanged;
            controller.ConfigChanged += controller_ConfigChanged;
            controller.PACFileReadyToOpen += controller_FileReadyToOpen;
            controller.UserRuleFileReadyToOpen += controller_FileReadyToOpen;
            controller.ShareOverLANStatusChanged += controller_ShareOverLANStatusChanged;
            controller.VerboseLoggingStatusChanged += controller_VerboseLoggingStatusChanged;
            controller.ShowPluginOutputChanged += controller_ShowPluginOutputChanged;
            controller.EnableGlobalChanged += controller_EnableGlobalChanged;
            controller.Errored += controller_Errored;
            controller.UpdatePACFromGeositeCompleted += controller_UpdatePACFromGeositeCompleted;
            controller.UpdatePACFromGeositeError += controller_UpdatePACFromGeositeError;

            _notifyIcon = new NotifyIcon();
            UpdateTrayIconAndNotifyText();
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = contextMenu1;
            _notifyIcon.BalloonTipClicked += notifyIcon1_BalloonTipClicked;
            _notifyIcon.MouseClick += notifyIcon1_Click;
            _notifyIcon.MouseDoubleClick += notifyIcon1_DoubleClick;
            _notifyIcon.BalloonTipClosed += _notifyIcon_BalloonTipClosed;
            controller.TrafficChanged += controller_TrafficChanged;

            LoadCurrentConfiguration();

            Configuration config = controller.GetCurrentConfiguration();
            if (config.firstRun)
            {
                ShowConfigForm();
            }
        }

        #region Tray Icon

        private void UpdateTrayIconAndNotifyText()
        {
            Configuration config = controller.GetCurrentConfiguration();
            bool enabled = config.enabled;
            bool global = config.global;

            Color colorMask = SelectColorMask(enabled, global);
            Size iconSize = SelectIconSize();

            UpdateIconSet(colorMask, iconSize, out icon, out icon_in, out icon_out, out icon_both);

            previousIcon = icon;
            _notifyIcon.Icon = previousIcon;

            string serverInfo = null;
            if (controller.GetCurrentStrategy() != null)
            {
                serverInfo = controller.GetCurrentStrategy().Name;
            }
            else
            {
                serverInfo = config.GetCurrentServer().ToString();
            }
            // show more info by hacking the P/Invoke declaration for NOTIFYICONDATA inside Windows Forms
            string text = I18N.GetString("Shadowsocks") + " " + VersionInfo.Version + "\n" +
                          (enabled ?
                              I18N.GetString("System Proxy On: ") + (global ? I18N.GetString("Global") : I18N.GetString("PAC")) :
                              I18N.GetString("Running: Port {0}", config.localPort))  // this feedback is very important because they need to know Shadowsocks is running
                          + "\n" + serverInfo;
            if (text.Length > 127)
            {
                text = text.Substring(0, 126 - 3) + "...";
            }
            ViewUtils.SetNotifyIconText(_notifyIcon, text);
        }

        /// <summary>
        /// Determine the icon size based on the screen DPI.
        /// </summary>
        /// <returns></returns>
        /// https://stackoverflow.com/a/40851713/2075611
        private Size SelectIconSize()
        {
            Size size = new Size(32, 32);
            int dpi = ViewUtils.GetScreenDpi();
            if (dpi < 97)
            {
                // dpi = 96;
                size = new Size(16, 16);
            }
            else if (dpi < 121)
            {
                // dpi = 120;
                size = new Size(20, 20);
            }
            else if (dpi < 145)
            {
                // dpi = 144;
                size = new Size(24, 24);
            }
            else
            {
                // dpi = 168;
                size = new Size(28, 28);
            }
            return size;
        }

        private Color SelectColorMask(bool isProxyEnabled, bool isGlobalProxy)
        {
            Color colorMask = Color.White;

            Utils.WindowsThemeMode currentWindowsThemeMode = Utils.GetWindows10SystemThemeSetting();

            if (isProxyEnabled)
            {
                if (isGlobalProxy)  // global
                {
                    colorMask = colorMaskBlue;
                }
                else  // PAC
                {
                    if (currentWindowsThemeMode == Utils.WindowsThemeMode.Light)
                    {
                        colorMask = colorMaskEclipse;
                    }
                }
            }
            else  // disabled
            {
                if (currentWindowsThemeMode == Utils.WindowsThemeMode.Light)
                {
                    colorMask = colorMaskDarkSilver;
                }
                else
                {
                    colorMask = colorMaskLightSilver;
                }
            }

            return colorMask;
        }

        private void UpdateIconSet(Color colorMask, Size size,
            out Icon icon, out Icon icon_in, out Icon icon_out, out Icon icon_both)
        {
            Bitmap iconBitmap;

            // generate the base icon
            iconBitmap = ViewUtils.ChangeBitmapColor(Resources.ss32Fill, colorMask);
            iconBitmap = ViewUtils.AddBitmapOverlay(iconBitmap, Resources.ss32Outline);

            icon = Icon.FromHandle(ViewUtils.ResizeBitmap(iconBitmap, size.Width, size.Height).GetHicon());
            icon_in = Icon.FromHandle(ViewUtils.ResizeBitmap(ViewUtils.AddBitmapOverlay(iconBitmap, Resources.ss32In), size.Width, size.Height).GetHicon());
            icon_out = Icon.FromHandle(ViewUtils.ResizeBitmap(ViewUtils.AddBitmapOverlay(iconBitmap, Resources.ss32In), size.Width, size.Height).GetHicon());
            icon_both = Icon.FromHandle(ViewUtils.ResizeBitmap(ViewUtils.AddBitmapOverlay(iconBitmap, Resources.ss32In, Resources.ss32Out), size.Width, size.Height).GetHicon());
        }

        #endregion

        #region MenuItems and MenuGroups

        private ToolStripMenuItem CreateMenuItem(string text, EventHandler click)
        {
            return new ToolStripMenuItem(I18N.GetString(text), null,
                (sender, args) => ErrorReporter.SafeExecute(() => click(sender, args), text));
        }

        private ToolStripMenuItem CreateMenuGroup(string text, ToolStripItem[] items)
        {
            return new ToolStripMenuItem(I18N.GetString(text), null, items);
        }

        private void LoadMenu()
        {
            this.contextMenu1 = new ContextMenuStrip();
            this.contextMenu1.Items.AddRange(new ToolStripItem[] {
                CreateMenuGroup("System Proxy", new ToolStripItem[] {
                    this.disableItem = CreateMenuItem("Disable", new EventHandler(this.EnableItem_Click)),
                    this.PACModeItem = CreateMenuItem("PAC", new EventHandler(this.PACModeItem_Click)),
                    this.globalModeItem = CreateMenuItem("Global", new EventHandler(this.GlobalModeItem_Click))
                }),
                this.ServersItem = CreateMenuGroup("Servers", new ToolStripItem[] {
                    this.SeperatorItem = new ToolStripSeparator(),
                    this.ConfigItem = CreateMenuItem("Edit Servers...", new EventHandler(this.Config_Click)),
                    new ToolStripSeparator(),
                    CreateMenuItem("Share Server Config...", new EventHandler(this.QRCodeItem_Click)),
                    CreateMenuItem("Scan QRCode from Screen...", new EventHandler(this.ScanQRCodeItem_Click)),
                    CreateMenuItem("Import URL from Clipboard...", new EventHandler(this.ImportURLItem_Click))
                }),
                CreateMenuGroup("PAC ", new ToolStripItem[] {
                    this.localPACItem = CreateMenuItem("Local PAC", new EventHandler(this.LocalPACItem_Click)),
                    this.onlinePACItem = CreateMenuItem("Online PAC", new EventHandler(this.OnlinePACItem_Click)),
                    new ToolStripSeparator(),
                    this.editLocalPACItem = CreateMenuItem("Edit Local PAC File...", new EventHandler(this.EditPACFileItem_Click)),
                    this.updateFromGeositeItem = CreateMenuItem("Update Local PAC from Geosite", new EventHandler(this.UpdatePACFromGeositeItem_Click)),
                    this.editGFWUserRuleItem = CreateMenuItem("Edit User Rule for Geosite...", new EventHandler(this.EditUserRuleFileForGeositeItem_Click)),
                    this.secureLocalPacUrlToggleItem = CreateMenuItem("Secure Local PAC", new EventHandler(this.SecureLocalPacUrlToggleItem_Click)),
                    this.regenerateLocalPacOnUpdateItem = CreateMenuItem("Regenerate local PAC on version update", new EventHandler(this.RegenerateLocalPacOnUpdateItem_Click)),
                    CreateMenuItem("Copy Local PAC URL", new EventHandler(this.CopyLocalPacUrlItem_Click)),
                    this.editOnlinePACItem = CreateMenuItem("Edit Online PAC URL...", new EventHandler(this.UpdateOnlinePACURLItem_Click)),
                }),
                this.proxyItem = CreateMenuItem("Forward Proxy...", new EventHandler(this.proxyItem_Click)),
                this.onlineConfigItem = CreateMenuItem("Online Config...", new EventHandler(this.OnlineConfig_Click)),
                new ToolStripSeparator(),
                this.AutoStartupItem = CreateMenuItem("Start on Boot", new EventHandler(this.AutoStartupItem_Click)),
                this.ProtocolHandlerItem = CreateMenuItem("Associate ss:// Links", new EventHandler(this.ProtocolHandlerItem_Click)),
                this.ShareOverLANItem = CreateMenuItem("Allow other Devices to connect", new EventHandler(this.ShareOverLANItem_Click)),
                new ToolStripSeparator(),
                this.hotKeyItem = CreateMenuItem("Edit Hotkeys...", new EventHandler(this.hotKeyItem_Click)),
                CreateMenuGroup("Help", new ToolStripItem[] {
                    CreateMenuItem("Show Logs...", new EventHandler(this.ShowLogItem_Click)),
                    this.VerboseLoggingToggleItem = CreateMenuItem( "Verbose Logging", new EventHandler(this.VerboseLoggingToggleItem_Click) ),
                    this.ShowPluginOutputToggleItem = CreateMenuItem("Show Plugin Output", new EventHandler(this.ShowPluginOutputToggleItem_Click)),
                    CreateMenuItem("About...", new EventHandler(this.AboutItem_Click)),
                }),
                new ToolStripSeparator(),
                CreateMenuItem("Quit", new EventHandler(this.Quit_Click))
            });
        }

        #endregion

        private void controller_TrafficChanged(object sender, EventArgs e)
        {
            if (icon == null)
                return;

            Icon newIcon;

            bool hasInbound = controller.trafficPerSecondQueue.Last().inboundIncreasement > 0;
            bool hasOutbound = controller.trafficPerSecondQueue.Last().outboundIncreasement > 0;

            if (hasInbound && hasOutbound)
                newIcon = icon_both;
            else if (hasInbound)
                newIcon = icon_in;
            else if (hasOutbound)
                newIcon = icon_out;
            else
                newIcon = icon;

            if (newIcon != this.previousIcon)
            {
                this.previousIcon = newIcon;
                _notifyIcon.Icon = newIcon;
            }
        }

        void controller_Errored(object sender, ErrorEventArgs e)
        {
            MessageBox.Show(e.GetException().ToString(), I18N.GetString("Shadowsocks Error: {0}", e.GetException().Message));
        }

        private void controller_ConfigChanged(object sender, EventArgs e)
        {
            LoadCurrentConfiguration();
            UpdateTrayIconAndNotifyText();
        }

        private void LoadCurrentConfiguration()
        {
            Configuration config = controller.GetCurrentConfiguration();
            UpdateServersMenu();
            UpdateSystemProxyItemsEnabledStatus(config);
            ShareOverLANItem.Checked = config.shareOverLan;
            VerboseLoggingToggleItem.Checked = config.isVerboseLogging;
            ShowPluginOutputToggleItem.Checked = config.showPluginOutput;
            AutoStartupItem.Checked = AutoStartup.Check();
            ProtocolHandlerItem.Checked = ProtocolHandler.Check();
            onlinePACItem.Checked = onlinePACItem.Enabled && config.useOnlinePac;
            localPACItem.Checked = !onlinePACItem.Checked;
            secureLocalPacUrlToggleItem.Checked = config.secureLocalPac;
            regenerateLocalPacOnUpdateItem.Checked = config.regeneratePacOnUpdate;
            UpdatePACItemsEnabledStatus();
        }

        #region Forms

        private void ShowConfigForm()
        {
            try
            {
                if (configForm != null && !configForm.IsDisposed)
                {
                    configForm.Activate();
                    return;
                }
                configForm = null;
                var form = new ConfigForm(controller);
                form.FormClosed += configForm_FormClosed;
                configForm = form;
                form.Show();
                form.Activate();
            }
            catch (Exception e)
            {
                logger.Error(e);
                MessageBox.Show(e.ToString(), I18N.GetString("Shadowsocks Error: {0}", e.Message));
            }
        }

        private void ShowLogForm()
        {
            try
            {
                if (logForm != null && !logForm.IsDisposed)
                {
                    logForm.Activate();
                    return;
                }
                logForm = null;
                var form = new LogForm(controller);
                form.FormClosed += logForm_FormClosed;
                logForm = form;
                form.Show();
                form.Activate();
            }
            catch (Exception e)
            {
                logger.Error(e);
                MessageBox.Show(e.ToString(), I18N.GetString("Shadowsocks Error: {0}", e.Message));
            }
        }

        void logForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (ReferenceEquals(logForm, sender))
            {
                logForm = null;
            }
        }

        void configForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (ReferenceEquals(configForm, sender))
            {
                configForm = null;
            }
            var config = controller.GetCurrentConfiguration();
            if (config.firstRun)
            {
                ShowBalloonTip(
                    I18N.GetString("Shadowsocks is here"),
                    I18N.GetString("You can turn on/off Shadowsocks in the context menu"),
                    ToolTipIcon.Info,
                    0
                );
                config.firstRun = false;
            }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, I18N.GetString("Shadowsocks Error"));
            }
        }

        #endregion

        #region Misc

        void ShowBalloonTip(string title, string content, ToolTipIcon icon, int timeout)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = content;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(timeout);
        }

        void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
        }

        private void _notifyIcon_BalloonTipClosed(object sender, EventArgs e)
        {
        }

        private void notifyIcon1_Click(object sender, MouseEventArgs e)
        {
            UpdateTrayIconAndNotifyText();
            if (e.Button == MouseButtons.Middle)
            {
                ShowLogForm();
            }
        }

        private void notifyIcon1_DoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowConfigForm();
            }
        }

        public void ShowLogForm_HotKey()
        {
            ShowLogForm();
        }

        #endregion

        #region Main menu

        void controller_ShareOverLANStatusChanged(object sender, EventArgs e)
        {
            ShareOverLANItem.Checked = controller.GetCurrentConfiguration().shareOverLan;
        }

        private void proxyItem_Click(object sender, EventArgs e)
        {
            if (forwardProxyForm == null || forwardProxyForm.IsDisposed)
            {
                forwardProxyForm = new ForwardProxyForm(controller);
                forwardProxyForm.FormClosed += (s, args) => { if (ReferenceEquals(forwardProxyForm, s)) forwardProxyForm = null; };
                forwardProxyForm.Show();
            }
            forwardProxyForm.Activate();
        }

        private void OnlineConfig_Click(object sender, EventArgs e)
        {
            if (onlineConfigForm == null || onlineConfigForm.IsDisposed)
            {
                onlineConfigForm = new OnlineConfigForm(controller);
                onlineConfigForm.FormClosed += (s, args) => { if (ReferenceEquals(onlineConfigForm, s)) onlineConfigForm = null; };
                onlineConfigForm.Show();
            }
            onlineConfigForm.Activate();
        }

        private void hotKeyItem_Click(object sender, EventArgs e)
        {
            if (hotkeysForm == null || hotkeysForm.IsDisposed)
            {
                hotkeysForm = new HotkeysForm(controller);
                hotkeysForm.FormClosed += (s, args) => { if (ReferenceEquals(hotkeysForm, s)) hotkeysForm = null; };
                hotkeysForm.Show();
            }
            hotkeysForm.Activate();
        }

        private void ShareOverLANItem_Click(object sender, EventArgs e)
        {
            ShareOverLANItem.Checked = !ShareOverLANItem.Checked;
            controller.ToggleShareOverLAN(ShareOverLANItem.Checked);
        }

        private void AutoStartupItem_Click(object sender, EventArgs e)
        {
            AutoStartupItem.Checked = !AutoStartupItem.Checked;
            if (!AutoStartup.Set(AutoStartupItem.Checked))
            {
                MessageBox.Show(I18N.GetString("Failed to update registry"));
            }
            LoadCurrentConfiguration();
        }

        private void ProtocolHandlerItem_Click(object sender, EventArgs e)
        {
            ProtocolHandlerItem.Checked = !ProtocolHandlerItem.Checked;
            if (!ProtocolHandler.Set(ProtocolHandlerItem.Checked))
            {
                MessageBox.Show(I18N.GetString("Failed to update registry"));
            }
            LoadCurrentConfiguration();
        }

        private void Quit_Click(object sender, EventArgs e)
        {
            controller.Stop();
            _notifyIcon.Visible = false;
            Application.Exit();
        }

        #endregion

        #region System proxy

        private void controller_EnableStatusChanged(object sender, EventArgs e)
        {
            disableItem.Checked = !controller.GetCurrentConfiguration().enabled;
        }

        private void EnableItem_Click(object sender, EventArgs e)
        {
            controller.ToggleEnable(false);
            Configuration config = controller.GetCurrentConfiguration();
            UpdateSystemProxyItemsEnabledStatus(config);
        }

        void controller_EnableGlobalChanged(object sender, EventArgs e)
        {
            globalModeItem.Checked = controller.GetCurrentConfiguration().global;
            PACModeItem.Checked = !globalModeItem.Checked;
        }

        private void UpdateSystemProxyItemsEnabledStatus(Configuration config)
        {
            disableItem.Checked = !config.enabled;
            if (!config.enabled)
            {
                globalModeItem.Checked = false;
                PACModeItem.Checked = false;
            }
            else
            {
                globalModeItem.Checked = config.global;
                PACModeItem.Checked = !config.global;
            }
        }

        private void GlobalModeItem_Click(object sender, EventArgs e)
        {
            controller.ToggleEnable(true);
            controller.ToggleGlobal(true);
            Configuration config = controller.GetCurrentConfiguration();
            UpdateSystemProxyItemsEnabledStatus(config);
        }

        private void PACModeItem_Click(object sender, EventArgs e)
        {
            controller.ToggleEnable(true);
            controller.ToggleGlobal(false);
            Configuration config = controller.GetCurrentConfiguration();
            UpdateSystemProxyItemsEnabledStatus(config);
        }

        #endregion

        #region Server

        private void UpdateServersMenu()
        {
            var items = ServersItem.DropDownItems;
            while (items[0] != SeperatorItem)
            {
                items.RemoveAt(0);
            }
            Configuration configuration = controller.GetCurrentConfiguration();
            bool hasHighAvailabilityServers = configuration.configs.Any(server => server.highAvailability);

            int strategyCount = 0;
            foreach (var strategy in controller.GetStrategies())
            {
                ToolStripMenuItem item = new ToolStripMenuItem(strategy.Name);
                item.Tag = strategy.ID;
                item.Click += (s, e) => ErrorReporter.SafeExecute(() => AStrategyItem_Click(s, e), strategy.Name);
                if (strategy.ID == HighAvailabilityStrategy.StrategyID && !hasHighAvailabilityServers)
                {
                    item.Enabled = false;
                    item.ToolTipText = I18N.GetString("No server is enabled for high availability");
                }
                items.Insert(strategyCount, item);
                strategyCount++;
            }

            // user wants a seperator item between strategy and servers menugroup
            items.Insert(strategyCount++, new ToolStripSeparator());

            int maxCount = 20;
            int serverCount = 0;
            bool overflow = false;
            bool needAdd = true;
            
            for (int i = 0; i < configuration.configs.Count; i++)
            {
                try
                {
                    if (overflow)
                    {
                        needAdd = configuration.index >= i;
                        if (needAdd)
                        {
                            i = configuration.index;
                        }
                    }

                    if (needAdd)
                    {
                        var server = configuration.configs[i];
                        Configuration.CheckServer(server);
                        var item = new ToolStripMenuItem(server.ToString());
                        item.Tag = i;
                        item.Click += (s, e) => ErrorReporter.SafeExecute(() => AServerItem_Click(s, e), server.ToString());
                        items.Insert(strategyCount + serverCount, item);
                        serverCount++;
                    }
                    
                    if (overflow)
                    {
                        items.Insert(strategyCount + serverCount, new ToolStripMenuItem($"... more than {maxCount} (total {configuration.configs.Count})", null, Config_Click));
                        break;
                    }

                    overflow = serverCount > maxCount;
                }
                catch
                {
                }
            }

            foreach (ToolStripItem item in items)
            {
                if (item is ToolStripMenuItem menuItem && item.Tag != null &&
                    (item.Tag.ToString() == configuration.index.ToString() || item.Tag.ToString() == configuration.strategy))
                {
                    menuItem.Checked = true;
                }
            }
        }

        private void AServerItem_Click(object sender, EventArgs e)
        {
            var item = (ToolStripItem)sender;
            controller.SelectServerIndex((int)item.Tag);
        }

        private void AStrategyItem_Click(object sender, EventArgs e)
        {
            var item = (ToolStripItem)sender;
            string strategyID = (string)item.Tag;
            if (strategyID == HighAvailabilityStrategy.StrategyID &&
                !controller.GetCurrentConfiguration().configs.Any(server => server.highAvailability))
            {
                MessageBox.Show(I18N.GetString("No server is enabled for high availability"),
                    I18N.GetString("Shadowsocks"));
                return;
            }
            controller.SelectStrategy(strategyID);
        }

        private void Config_Click(object sender, EventArgs e)
        {
            ShowConfigForm();
        }

        void openURLFromQRCode()
        {
            OpenUrl(_urlToOpen);
        }

        private void QRCodeItem_Click(object sender, EventArgs e)
        {
            if (serverSharingForm == null || serverSharingForm.IsDisposed)
            {
                serverSharingForm = new ServerSharingForm(controller);
                serverSharingForm.FormClosed += (s, args) => { if (ReferenceEquals(serverSharingForm, s)) serverSharingForm = null; };
                serverSharingForm.Show();
            }
            serverSharingForm.Activate();
        }

        private void ScanQRCodeItem_Click(object sender, EventArgs e)
        {
            var result = Utils.ScanQRCodeFromScreen();
            if (result != null)
            {
                if (result.ToLowerInvariant().StartsWith("http://") || result.ToLowerInvariant().StartsWith("https://"))
                {
                    _urlToOpen = result;
                    openURLFromQRCode();
                }
                else if (controller.AddServerBySSURL(result))
                {
                    ShowConfigForm();
                }
                else
                {
                    MessageBox.Show(I18N.GetString("Invalid QR Code content: {0}", result));
                }
                return;
            }
            else
                MessageBox.Show(I18N.GetString("No QRCode found. Try to zoom in or move it to the center of the screen."));
        }

        private void ImportURLItem_Click(object sender, EventArgs e)
        {
            if (controller.AskAddServerBySSURL(Clipboard.GetText(TextDataFormat.Text)))
            {
                ShowConfigForm();
            }
        }

        #endregion

        #region PAC

        private void LocalPACItem_Click(object sender, EventArgs e)
        {
            if (!localPACItem.Checked)
            {
                localPACItem.Checked = true;
                onlinePACItem.Checked = false;
                controller.UseOnlinePAC(false);
                UpdatePACItemsEnabledStatus();
            }
        }

        private void OnlinePACItem_Click(object sender, EventArgs e)
        {
            if (!onlinePACItem.Checked)
            {
                if (string.IsNullOrEmpty(controller.GetCurrentConfiguration().pacUrl))
                {
                    UpdateOnlinePACURLItem_Click(sender, e);
                }
                if (!string.IsNullOrEmpty(controller.GetCurrentConfiguration().pacUrl))
                {
                    localPACItem.Checked = false;
                    onlinePACItem.Checked = true;
                    controller.UseOnlinePAC(true);
                }
                UpdatePACItemsEnabledStatus();
            }
        }

        private void UpdateOnlinePACURLItem_Click(object sender, EventArgs e)
        {
            string origPacUrl = controller.GetCurrentConfiguration().pacUrl;
            string pacUrl = Microsoft.VisualBasic.Interaction.InputBox(
                I18N.GetString("Please input PAC Url"),
                I18N.GetString("Edit Online PAC URL"),
                origPacUrl, -1, -1);
            if (!string.IsNullOrEmpty(pacUrl) && pacUrl != origPacUrl)
            {
                controller.SavePACUrl(pacUrl);
            }
        }

        private void SecureLocalPacUrlToggleItem_Click(object sender, EventArgs e)
        {
            Configuration configuration = controller.GetCurrentConfiguration();
            controller.ToggleSecureLocalPac(!configuration.secureLocalPac);
        }

        private void RegenerateLocalPacOnUpdateItem_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            controller.ToggleRegeneratePacOnUpdate(!config.regeneratePacOnUpdate);
        }

        private void CopyLocalPacUrlItem_Click(object sender, EventArgs e)
        {
            controller.CopyPacUrl();
        }

        private void UpdatePACItemsEnabledStatus()
        {
            if (this.localPACItem.Checked)
            {
                this.editLocalPACItem.Enabled = true;
                this.updateFromGeositeItem.Enabled = true;
                this.editGFWUserRuleItem.Enabled = true;
                this.editOnlinePACItem.Enabled = false;
            }
            else
            {
                this.editLocalPACItem.Enabled = false;
                this.updateFromGeositeItem.Enabled = false;
                this.editGFWUserRuleItem.Enabled = false;
                this.editOnlinePACItem.Enabled = true;
            }
        }

        private void EditPACFileItem_Click(object sender, EventArgs e)
        {
            controller.TouchPACFile();
        }

        private async void UpdatePACFromGeositeItem_Click(object sender, EventArgs e)
        {
            await GeositeUpdater.UpdatePACFromGeosite();
        }

        private void EditUserRuleFileForGeositeItem_Click(object sender, EventArgs e)
        {
            controller.TouchUserRuleFile();
        }

        void controller_FileReadyToOpen(object sender, ShadowsocksController.PathEventArgs e)
        {
            string argument = @"/select, " + e.Path;

            Process.Start("explorer.exe", argument);
        }

        void controller_UpdatePACFromGeositeError(object sender, System.IO.ErrorEventArgs e)
        {
            ShowBalloonTip(I18N.GetString("Failed to update PAC file"), e.GetException().Message, ToolTipIcon.Error, 5000);
            logger.LogUsefulException(e.GetException());
        }

        void controller_UpdatePACFromGeositeCompleted(object sender, GeositeResultEventArgs e)
        {
            string result = e.Success
                ? I18N.GetString("PAC updated")
                : I18N.GetString("No updates found. Please report to Geosite if you have problems with it.");
            ShowBalloonTip(I18N.GetString("Shadowsocks"), result, ToolTipIcon.Info, 1000);
        }

        #endregion

        #region Help

        void controller_VerboseLoggingStatusChanged(object sender, EventArgs e)
        {
            VerboseLoggingToggleItem.Checked = controller.GetCurrentConfiguration().isVerboseLogging;
        }

        void controller_ShowPluginOutputChanged(object sender, EventArgs e)
        {
            ShowPluginOutputToggleItem.Checked = controller.GetCurrentConfiguration().showPluginOutput;
        }

        private void VerboseLoggingToggleItem_Click(object sender, EventArgs e)
        {
            VerboseLoggingToggleItem.Checked = !VerboseLoggingToggleItem.Checked;
            controller.ToggleVerboseLogging(VerboseLoggingToggleItem.Checked);
        }

        private void ShowLogItem_Click(object sender, EventArgs e)
        {
            ShowLogForm();
        }

        private void ShowPluginOutputToggleItem_Click(object sender, EventArgs e)
        {
            ShowPluginOutputToggleItem.Checked = !ShowPluginOutputToggleItem.Checked;
            controller.ToggleShowPluginOutput(ShowPluginOutputToggleItem.Checked);
        }

        #endregion

        private void AboutItem_Click(object sender, EventArgs e)
        {
            string details =
                $@"{I18N.GetString("Shadowsocks")} {VersionInfo.Version}
.NET: {RuntimeInformation.FrameworkDescription}
Architecture: {RuntimeInformation.ProcessArchitecture}
OS: {RuntimeInformation.OSDescription}

License: GPLv3";
            MessageBox.Show(details, I18N.GetString("About..."), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
