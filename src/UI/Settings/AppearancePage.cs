using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class AppearancePage : SettingsPageBase
    {
        private Panel _container;
        private bool _isLoaded = false;

        // 控件缓存
        private ComboBox _cmbTheme;
        private ComboBox _cmbOrientation;
        private ComboBox _cmbWidth;
        private ComboBox _cmbOpacity;
        private ComboBox _cmbScale;
        private ComboBox _cmbRefresh;
        
        private LiteCheck _chkTaskbarCompact;
        private LiteCheck _chkTaskbarAlignLeft;

        public AppearancePage()
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

            CreateThemeCard();
            CreateDisplayCard();
            CreatePerfCard();
            CreateTaskbarCard();

            _container.ResumeLayout();
            _isLoaded = true;
        }

        // === 1. 主题与布局 ===
        private void CreateThemeCard()
        {
            var card = CreateCard("主题与布局 (Style & Layout)");
            var flow = card.Controls[0] as FlowLayoutPanel;

            // --- Theme ---
            var pnlTheme = CreateRowPanel();
            pnlTheme.Controls.Add(CreateLabel("皮肤主题 / Theme", 20, 12));
            _cmbTheme = CreateCombo();
            // 获取所有可用主题
            foreach (var t in ThemeManager.GetAvailableThemes()) _cmbTheme.Items.Add(t);
            // 选中当前
            if (_cmbTheme.Items.Contains(Config.Skin)) _cmbTheme.SelectedItem = Config.Skin;
            else if (_cmbTheme.Items.Count > 0) _cmbTheme.SelectedIndex = 0;
            
            pnlTheme.Controls.Add(_cmbTheme);
            flow.Controls.Add(pnlTheme);

            // --- Orientation ---
            var pnlMode = CreateRowPanel();
            pnlMode.Controls.Add(CreateLabel("显示方向 / Orientation", 20, 12));
            _cmbOrientation = CreateCombo();
            _cmbOrientation.Items.Add("Vertical (竖向)");
            _cmbOrientation.Items.Add("Horizontal (横向)");
            _cmbOrientation.SelectedIndex = Config.HorizontalMode ? 1 : 0;
            
            pnlMode.Controls.Add(_cmbOrientation);
            flow.Controls.Add(pnlMode);

            // --- Width ---
            var pnlWidth = CreateRowPanel();
            pnlWidth.Controls.Add(CreateLabel("面板宽度 / Width", 20, 12));
            _cmbWidth = CreateCombo();
            int[] widths = { 180, 200, 220, 240, 260, 280, 300, 360, 420, 480, 600, 800 };
            foreach (var w in widths) _cmbWidth.Items.Add(w + " px");
            
            // 尝试选中最接近的值
            string curW = Config.PanelWidth + " px";
            if (!_cmbWidth.Items.Contains(curW)) _cmbWidth.Items.Add(curW); // 如果是自定义值，加进去
            _cmbWidth.SelectedItem = curW;

            pnlWidth.Controls.Add(_cmbWidth);
            flow.Controls.Add(pnlWidth);

            AddCardToPage(card);
        }

        // === 2. 显示缩放 ===
        private void CreateDisplayCard()
        {
            var card = CreateCard("显示缩放 (Display & Scale)");
            var flow = card.Controls[0] as FlowLayoutPanel;

            // --- UI Scale ---
            var pnlScale = CreateRowPanel();
            pnlScale.Controls.Add(CreateLabel("界面缩放 / UI Scale", 20, 12));
            _cmbScale = CreateCombo();
            double[] scales = { 0.5, 0.75, 0.9, 1.0, 1.25, 1.5, 1.75, 2.0 };
            foreach (var s in scales) _cmbScale.Items.Add((s * 100) + "%");
            
            string curS = (Config.UIScale * 100) + "%";
            SetComboVal(_cmbScale, curS);

            pnlScale.Controls.Add(_cmbScale);
            flow.Controls.Add(pnlScale);

            // --- Opacity ---
            var pnlOp = CreateRowPanel();
            pnlOp.Controls.Add(CreateLabel("不透明度 / Opacity", 20, 12));
            _cmbOpacity = CreateCombo();
            for (int i = 100; i >= 30; i -= 10) _cmbOpacity.Items.Add(i + "%");
            
            string curOp = Math.Round(Config.Opacity * 100) + "%";
            SetComboVal(_cmbOpacity, curOp);

            pnlOp.Controls.Add(_cmbOpacity);
            flow.Controls.Add(pnlOp);

            AddCardToPage(card);
        }

        // === 3. 性能 ===
        private void CreatePerfCard()
        {
            var card = CreateCard("性能 (Performance)");
            var flow = card.Controls[0] as FlowLayoutPanel;

            var pnlRef = CreateRowPanel();
            pnlRef.Controls.Add(CreateLabel("刷新频率 / Refresh Rate", 20, 12));
            _cmbRefresh = CreateCombo();
            int[] rates = { 100, 200, 500, 1000, 2000, 3000 };
            foreach (var r in rates) _cmbRefresh.Items.Add(r + " ms");
            
            SetComboVal(_cmbRefresh, Config.RefreshMs + " ms");

            pnlRef.Controls.Add(_cmbRefresh);
            flow.Controls.Add(pnlRef);

            AddCardToPage(card);
        }

        // === 4. 任务栏样式 ===
        private void CreateTaskbarCard()
        {
            var card = CreateCard("任务栏样式 (Taskbar Style)");
            var flow = card.Controls[0] as FlowLayoutPanel;

            // Compact Logic: 字体小且不加粗视为简洁模式
            bool isCompact = (Math.Abs(Config.TaskbarFontSize - 9f) < 0.1f) && !Config.TaskbarFontBold;

            _chkTaskbarCompact = CreateCheckRow(flow, "简洁模式 (小字体) / Compact Mode", isCompact);
            _chkTaskbarAlignLeft = CreateCheckRow(flow, "靠左对齐 (Win11) / Align Left", Config.TaskbarAlignLeft);

            // 提示文本
            var tips = new Label { 
                Text = "提示：对齐选项仅在 Win11 任务栏居中时有效。", 
                AutoSize = true, ForeColor = Color.Gray, Font = new Font("Microsoft YaHei UI", 8F),
                Location = new Point(20, 5), Padding = new Padding(0,0,0,10)
            };
            var p = new Panel { AutoSize = true, Padding = new Padding(0,0,0,10) };
            p.Controls.Add(tips);
            flow.Controls.Add(p);

            AddCardToPage(card);
        }

        // === 辅助方法 (与 GeneralPage 保持一致) ===

        private void SetComboVal(ComboBox cmb, string val)
        {
            if (!cmb.Items.Contains(val)) cmb.Items.Insert(0, val);
            cmb.SelectedItem = val;
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
                Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                BackColor = Color.White, Padding = new Padding(0, 5, 0, 10), Width = 2000
            };
            card.Controls.Add(flow);
            card.Controls.Add(header);
            return card;
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
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F), Width = 140, Location = new Point(400, 8),
                BackColor = Color.White
            };
        }

        private LiteCheck CreateCheckRow(FlowLayoutPanel flow, string text, bool check)
        {
            var pnl = CreateRowPanel();
            var lbl = CreateLabel(text, 20, 12);
            var chk = new LiteCheck(check) { Location = new Point(450, 10), Text = "Enable" };
            pnl.Controls.Add(lbl);
            pnl.Controls.Add(chk);
            flow.Controls.Add(pnl);
            return chk;
        }

        private void AddCardToPage(LiteCard card)
        {
            var wrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 20) };
            wrapper.Controls.Add(card);
            _container.Controls.Add(wrapper);
            _container.Controls.SetChildIndex(wrapper, 0);
        }

        // === 核心：保存逻辑 ===
        public override void Save()
        {
            if (!_isLoaded) return;

            // 1. Theme
            if (_cmbTheme.SelectedItem != null) 
                Config.Skin = _cmbTheme.SelectedItem.ToString();
            
            // 2. Mode
            Config.HorizontalMode = (_cmbOrientation.SelectedIndex == 1);

            // 3. Width (解析 "240 px" -> 240)
            Config.PanelWidth = ParseInt(_cmbWidth.Text);

            // 4. Scale (解析 "100%" -> 1.0)
            Config.UIScale = ParsePercent(_cmbScale.Text);

            // 5. Opacity (解析 "85%" -> 0.85)
            Config.Opacity = ParsePercent(_cmbOpacity.Text);

            // 6. Refresh (解析 "1000 ms" -> 1000)
            Config.RefreshMs = ParseInt(_cmbRefresh.Text);
            if (Config.RefreshMs < 50) Config.RefreshMs = 1000; // 防呆

            // 7. Taskbar Settings
            if (_chkTaskbarCompact.Checked)
            {
                Config.TaskbarFontSize = 9f;
                Config.TaskbarFontBold = false;
            }
            else
            {
                Config.TaskbarFontSize = 10f;
                Config.TaskbarFontBold = true;
            }
            Config.TaskbarAlignLeft = _chkTaskbarAlignLeft.Checked;
        }

        // 简易解析器
        private int ParseInt(string s)
        {
            string clean = new string(s.Where(char.IsDigit).ToArray());
            return int.TryParse(clean, out int v) ? v : 0;
        }

        private double ParsePercent(string s)
        {
            int v = ParseInt(s);
            return v > 0 ? v / 100.0 : 1.0;
        }
    }
}