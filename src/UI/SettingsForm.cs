using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;

namespace LiteMonitor.src.UI
{
    public class SettingsForm : Form
    {
        private Settings _cfg;
        private UIController _ui;
        private MainForm _mainForm;
        
        private FlowLayoutPanel _pnlNavContainer; 
        private Panel _pnlContent;
        private Dictionary<string, SettingsPageBase> _pages = new Dictionary<string, SettingsPageBase>();
        private SettingsPageBase _currentPage;
        private string _currentKey = "";

        public SettingsForm() { InitializeComponent(); }
        public SettingsForm(Settings cfg, UIController ui, MainForm mainForm) : this() { _cfg = cfg; _ui = ui; _mainForm = mainForm; InitPages(); }

        private void InitializeComponent()
        {
            this.Size = new Size(820, 680);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Settings";
            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.BackColor = UIColors.MainBg;

            // === 1. 侧边栏 ===
            var pnlSidebar = new Panel { Dock = DockStyle.Left, Width = 160, BackColor = UIColors.SidebarBg };
            
            _pnlNavContainer = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Fill, 
                FlowDirection = FlowDirection.TopDown, 
                WrapContents = false, 
                Padding = new Padding(0, 20, 0, 0),
                BackColor = UIColors.SidebarBg
            };
            
            var line = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = UIColors.Border };
            pnlSidebar.Controls.Add(_pnlNavContainer);
            pnlSidebar.Controls.Add(line);
            this.Controls.Add(pnlSidebar);

            // === 2. 底部按钮 ===
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = UIColors.MainBg };
            pnlBottom.Paint += (s, e) => e.Graphics.DrawLine(new Pen(UIColors.Border), 0, 0, Width, 0);

            var flowBtns = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, 
                Padding = new Padding(0, 14, 20, 0), WrapContents = false, BackColor = Color.Transparent 
            };
            
            var btnOk = new LiteButton("OK", true);
            var btnCancel = new LiteButton("Cancel", false);
            var btnApply = new LiteButton("Apply", false);

            btnOk.Click += (s, e) => { ApplySettings(); this.DialogResult = DialogResult.OK; this.Close(); };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            btnApply.Click += (s, e) => { ApplySettings(); };

            flowBtns.Controls.Add(btnOk); flowBtns.Controls.Add(btnCancel); flowBtns.Controls.Add(btnApply);
            pnlBottom.Controls.Add(flowBtns);
            this.Controls.Add(pnlBottom);

            // === 3. 内容区 ===
            _pnlContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            this.Controls.Add(_pnlContent);
            
            pnlSidebar.BringToFront(); 
            pnlBottom.SendToBack(); 
            _pnlContent.BringToFront();
        }

        private void InitPages()
        {
            _pnlNavContainer.Controls.Clear();
            _pages.Clear();
            //AddNav("General", "基础设置", new SettingsPageBase()); // 占位
            // 在 InitPages() 中
            AddNav("General", "基础设置", new GeneralPage()); // 替换旧的 SettingsPageBase()
            // 2. 外观设置 (新增)
            AddNav("Appearance", "外观设置", new AppearancePage());
            // 3. 监控项显示 (新增)
            AddNav("Monitor", "监控项显示", new MonitorPage());
            // 4. 告警阈值设置 (新增)   
            AddNav("Threshold", "告警阈值设置", new ThresholdPage()); // ★ 新增这一行
            
            
            // 强制刷新一次布局，防止按钮不可见
            _pnlNavContainer.PerformLayout();
            
            SwitchPage("General");
        }

        private void AddNav(string key, string text, SettingsPageBase page)
        {
            page.SetConfig(_cfg);
            _pages[key] = page;
            var btn = new LiteNavBtn(text) { Tag = key };
            btn.Click += (s, e) => SwitchPage(key);
            _pnlNavContainer.Controls.Add(btn);
        }

        private void SwitchPage(string key)
        {
            if (_currentKey == key) return;
            _currentKey = key;

            // 更新侧边栏
            _pnlNavContainer.SuspendLayout();
            foreach (Control c in _pnlNavContainer.Controls)
                if (c is LiteNavBtn b) b.IsActive = ((string)b.Tag == key);
            _pnlNavContainer.ResumeLayout();
            _pnlNavContainer.Refresh(); 
            Application.DoEvents();

            // 更新内容
            if (_pages.ContainsKey(key))
            {
                _pnlContent.Controls.Clear();
                _currentPage = _pages[key];
                _currentPage.OnShow();
                _pnlContent.Controls.Add(_currentPage);
            }
        }

        private void ApplySettings()
        {
            foreach (var page in _pages.Values) page.Save();
            _cfg.Save();
            _cfg.SyncToLanguage();
            _ui.ApplyTheme(_cfg.Skin);
            _ui.RebuildLayout();
            _mainForm.RebuildMenus();
            foreach (Form f in Application.OpenForms) if (f is TaskbarForm tf) tf.ReloadLayout();
        }
    }
}