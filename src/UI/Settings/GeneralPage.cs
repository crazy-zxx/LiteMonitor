using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.System;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class GeneralPage : SettingsPageBase
    {
        private Panel _container;
        private bool _isLoaded = false;

        // 控件缓存，用于 Save 时读取
        private ComboBox _cmbLang;
        private LiteCheck _chkAutoStart;
        private LiteCheck _chkTopMost;
        
        private LiteCheck _chkAutoHide;
        private LiteCheck _chkClickThrough;
        private LiteCheck _chkClamp;
        private LiteCheck _chkHideTray;
        private LiteCheck _chkHideMain;

        private ComboBox _cmbNet;
        private ComboBox _cmbDisk;

        public GeneralPage()
        {
            this.BackColor = UIColors.MainBg;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);

            _container = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20)
            };
            this.Controls.Add(_container);
        }

        public override void OnShow()
        {
            if (Config == null || _isLoaded) return;

            _container.SuspendLayout();
            _container.Controls.Clear();

            // 1. 系统设置 (语言、自启、置顶)
            CreateSystemCard();

            // 2. 行为与交互 (自动隐藏、穿透等)
            CreateBehaviorCard();

            // 3. 硬件数据源 (网卡、磁盘)
            CreateSourceCard();

            _container.ResumeLayout();
            _isLoaded = true;
        }

        private void CreateSystemCard()
        {
            var card = CreateCard("系统设置 (System)");
            var flow = card.Controls[0] as FlowLayoutPanel;

            // --- 语言选择 ---
            var pnlLang = CreateRowPanel();
            pnlLang.Controls.Add(CreateLabel("语言 / Language", 20, 12));
            
            _cmbLang = CreateCombo();
            _cmbLang.Items.Add("English (en)"); // 默认
            // 扫描语言文件
            string langDir = Path.Combine(AppContext.BaseDirectory, "resources/lang");
            if (Directory.Exists(langDir))
            {
                foreach (var file in Directory.EnumerateFiles(langDir, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    _cmbLang.Items.Add(code.ToUpper());
                }
            }
            // 选中当前语言
            string curLang = string.IsNullOrEmpty(Config.Language) ? "en" : Config.Language;
            foreach (var item in _cmbLang.Items)
            {
                if (item.ToString().Contains(curLang.ToUpper())) 
                    _cmbLang.SelectedItem = item;
            }
            
            pnlLang.Controls.Add(_cmbLang);
            flow.Controls.Add(pnlLang);

            // --- 开关项 ---
            _chkAutoStart = CreateCheckRow(flow, "开机自启动 / Auto Start", Config.AutoStart);
            _chkTopMost = CreateCheckRow(flow, "窗口置顶 / Always on Top", Config.TopMost);

            AddCardToPage(card);
        }

        private void CreateBehaviorCard()
        {
            var card = CreateCard("交互与行为 (Behavior)");
            var flow = card.Controls[0] as FlowLayoutPanel;

            _chkAutoHide = CreateCheckRow(flow, "自动隐藏 (鼠标移开后) / Auto Hide", Config.AutoHide);
            _chkClickThrough = CreateCheckRow(flow, "鼠标穿透 (点击无效) / Click Through", Config.ClickThrough);
            _chkClamp = CreateCheckRow(flow, "限制在屏幕内 / Clamp to Screen", Config.ClampToScreen);
            
            AddSeparator(flow);

            // 进阶：显示控制
            _chkHideTray = CreateCheckRow(flow, "隐藏托盘图标 / Hide Tray Icon", Config.HideTrayIcon);
            _chkHideMain = CreateCheckRow(flow, "隐藏主界面 / Hide Main Window", Config.HideMainForm);

            // 添加简单的联动逻辑防止全部隐藏
            _chkHideTray.CheckedChanged += (s, e) => CheckVisibilitySafe();
            _chkHideMain.CheckedChanged += (s, e) => CheckVisibilitySafe();

            AddCardToPage(card);
        }

        private void CreateSourceCard()
        {
            var card = CreateCard("硬件来源 (Hardware Source)");
            var flow = card.Controls[0] as FlowLayoutPanel;

            // --- 磁盘 ---
            var pnlDisk = CreateRowPanel();
            pnlDisk.Controls.Add(CreateLabel("优先磁盘 / Disk Source", 20, 12));
            _cmbDisk = CreateCombo();
            _cmbDisk.Items.Add("Auto");
            foreach (var d in HardwareMonitor.ListAllDisks()) _cmbDisk.Items.Add(d);
            
            // 选中逻辑
            if (string.IsNullOrEmpty(Config.PreferredDisk)) _cmbDisk.SelectedIndex = 0;
            else if (_cmbDisk.Items.Contains(Config.PreferredDisk)) _cmbDisk.SelectedItem = Config.PreferredDisk;
            else _cmbDisk.SelectedIndex = 0;

            pnlDisk.Controls.Add(_cmbDisk);
            flow.Controls.Add(pnlDisk);

            // --- 网络 ---
            var pnlNet = CreateRowPanel();
            pnlNet.Controls.Add(CreateLabel("优先网卡 / Network Source", 20, 12));
            _cmbNet = CreateCombo();
            _cmbNet.Items.Add("Auto");
            foreach (var n in HardwareMonitor.ListAllNetworks()) _cmbNet.Items.Add(n);

            if (string.IsNullOrEmpty(Config.PreferredNetwork)) _cmbNet.SelectedIndex = 0;
            else if (_cmbNet.Items.Contains(Config.PreferredNetwork)) _cmbNet.SelectedItem = Config.PreferredNetwork;
            else _cmbNet.SelectedIndex = 0;

            pnlNet.Controls.Add(_cmbNet);
            flow.Controls.Add(pnlNet);

            AddCardToPage(card);
        }

        // === 辅助方法 ===

        private void CheckVisibilitySafe()
        {
            // 简单的防呆：如果任务栏也关了，不能同时隐藏托盘和主界面
            if (!Config.ShowTaskbar && _chkHideMain.Checked && _chkHideTray.Checked)
            {
                // 谁最后点的，谁取消
                if (_chkHideMain.Focused) _chkHideMain.Checked = false;
                else _chkHideTray.Checked = false;
            }
        }

        private LiteCard CreateCard(string title)
        {
            var card = new LiteCard { Dock = DockStyle.Top };
            var header = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = UIColors.GroupHeader };
            header.Paint += (s, e) => e.Graphics.DrawLine(new Pen(UIColors.Border), 0, 39, header.Width, 39);
            var lbl = new Label { Text = title, Location = new Point(15, 10), AutoSize = true, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = UIColors.TextMain };
            header.Controls.Add(lbl);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.White,
                Padding = new Padding(0, 5, 0, 10),
                Width = 2000
            };

            card.Controls.Add(flow);
            card.Controls.Add(header);
            return card;
        }

        private LiteCheck CreateCheckRow(FlowLayoutPanel flow, string text, bool check)
        {
            var pnl = CreateRowPanel();
            var lbl = CreateLabel(text, 20, 12);
            var chk = new LiteCheck(check) { Location = new Point(450, 10), Text = "Enable" }; // 统一位置
            pnl.Controls.Add(lbl);
            pnl.Controls.Add(chk);
            flow.Controls.Add(pnl);
            return chk;
        }

        private Panel CreateRowPanel()
        {
            var p = new Panel { Size = new Size(580, 40), Margin = new Padding(0) };
            p.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.WhiteSmoke), 20, 39, 560, 39);
            return p;
        }

        private Label CreateLabel(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = new Font("Microsoft YaHei UI", 9F), ForeColor = UIColors.TextMain };
        }

        private ComboBox CreateCombo()
        {
            var cmb = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Width = 140,
                Location = new Point(400, 8)
            };
            cmb.BackColor = Color.White;
            return cmb;
        }

        private void AddSeparator(FlowLayoutPanel flow)
        {
            var p = new Panel { Size = new Size(580, 10) }; // 留白
            flow.Controls.Add(p);
        }

        private void AddCardToPage(LiteCard card)
        {
            var wrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 20) };
            wrapper.Controls.Add(card);
            _container.Controls.Add(wrapper);
            _container.Controls.SetChildIndex(wrapper, 0);
        }

        public override void Save()
        {
            if (!_isLoaded) return;

            // 1. System
            Config.AutoStart = _chkAutoStart.Checked;
            Config.TopMost = _chkTopMost.Checked;
            
            // 简单的语言解析 "English (en)" -> "en"
            if (_cmbLang.SelectedItem != null)
            {
                string s = _cmbLang.SelectedItem.ToString();
                if (s.Contains("(") && s.Contains(")"))
                {
                    int start = s.LastIndexOf("(") + 1;
                    int len = s.LastIndexOf(")") - start;
                    if (len > 0) Config.Language = s.Substring(start, len).ToLower();
                }
                else if (s == "Auto") Config.Language = "";
            }

            // 2. Behavior
            Config.AutoHide = _chkAutoHide.Checked;
            Config.ClickThrough = _chkClickThrough.Checked;
            Config.ClampToScreen = _chkClamp.Checked;
            Config.HideTrayIcon = _chkHideTray.Checked;
            Config.HideMainForm = _chkHideMain.Checked;

            // 3. Source
            if (_cmbDisk.SelectedItem != null)
            {
                string d = _cmbDisk.SelectedItem.ToString();
                Config.PreferredDisk = (d == "Auto") ? "" : d;
            }
            if (_cmbNet.SelectedItem != null)
            {
                string n = _cmbNet.SelectedItem.ToString();
                Config.PreferredNetwork = (n == "Auto") ? "" : n;
            }

            // 特殊处理：保存开机启动
            AutoStart.Set(Config.AutoStart);
        }
    }
}