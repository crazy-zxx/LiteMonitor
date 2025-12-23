using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class MonitorPage : SettingsPageBase
    {
        private Panel _header;
        private Panel _container;
        private bool _isLoaded = false;

        // 坐标定义 (保持你认可的布局)
        private const int X_ID = 20;
        private const int X_NAME = 125;
        private const int X_SHORT = 265;
        private const int X_PANEL = 355;
        private const int X_TASKBAR = 430;
        private const int X_SORT = 520;

        private List<GroupUI> _groupsUI = new List<GroupUI>();
        private List<RowUI> _rowsUI = new List<RowUI>();

        public MonitorPage()
        {
            this.BackColor = UIColors.MainBg;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);

            _header = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = UIColors.MainBg };
            _header.Padding = new Padding(20, 0, 20 + SystemInformation.VerticalScrollBarWidth, 0);

            AddHead("监控项", X_ID);
            AddHead("名称", X_NAME);
            AddHead("简称", X_SHORT);
            AddHead("主界面显示", X_PANEL);
            AddHead("任务栏显示", X_TASKBAR);
            AddHead("排序", X_SORT);

            this.Controls.Add(_header);

            _container = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20, 35, 20, 20)
            };
            this.Controls.Add(_container);
            _header.BringToFront();
        }

        private void AddHead(string text, int x)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(x + 20, 10),
                AutoSize = true,
                ForeColor = UIColors.TextSub,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold)
            };
            _header.Controls.Add(lbl);
        }

        public override void OnShow()
        {
            if (Config == null) return;

            // ★★★ 核心优化：如果已经加载过，直接返回，保留用户修改的状态 ★★★
            if (_isLoaded) return; 

            _container.SuspendLayout();
            _container.Controls.Clear();
            _groupsUI.Clear();
            _rowsUI.Clear();

            var allItems = Config.MonitorItems.OrderBy(x => x.SortIndex).ToList();
            var groups = allItems.GroupBy(x => x.Key.Split('.')[0]);

            foreach (var g in groups.Reverse())
            {
                CreateGroupCard(g.Key, g.ToList());
            }
            _container.ResumeLayout();

            // 标记已加载
            _isLoaded = true;
        }

        private void CreateGroupCard(string groupKey, List<MonitorItemConfig> items)
        {
            var wrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 20) };
            var card = new LiteCard { Dock = DockStyle.Top };

            // 1. Rows Panel (子项容器)
            var rowsPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.White };

            // 2. Header Panel (组头)
            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = UIColors.GroupHeader };
            headerPanel.Paint += (s, e) => e.Graphics.DrawLine(new Pen(UIColors.Border), 0, 44, headerPanel.Width, 44);

            // --- 填充 Header ---
            var lblId = new Label { Text = groupKey, Location = new Point(X_ID, 12), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.Gray };

            string defGName = LanguageManager.T("Groups." + groupKey);
            if (defGName.StartsWith("Groups.")) defGName = groupKey;
            string alias = (Config.GroupAliases != null && Config.GroupAliases.ContainsKey(groupKey)) ? Config.GroupAliases[groupKey] : "";

            // ★ 修复：直接显示值，不判断空则变灰
            var inputGroup = new LiteUnderlineInput(string.IsNullOrEmpty(alias) ? defGName : alias);
            inputGroup.Location = new Point(X_NAME, 8);
            inputGroup.Size = new Size(120, 28);
            inputGroup.SetBg(UIColors.GroupHeader);
            inputGroup.Inner.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            inputGroup.Inner.ForeColor = UIColors.TextMain; // ★ 始终黑色

            var btnUp = new LiteSortBtn("▲") { Location = new Point(X_SORT-16, 10) };
            var btnDown = new LiteSortBtn("▼") { Location = new Point(X_SORT + 15, 10) };
            btnUp.Click += (s, e) => MoveGroup(wrapper, -1);
            btnDown.Click += (s, e) => MoveGroup(wrapper, 1);

            headerPanel.Controls.AddRange(new Control[] { lblId, inputGroup, btnUp, btnDown });

            // --- 填充 Rows ---
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var row = CreateRow(items[i], rowsPanel);
                rowsPanel.Controls.Add(row);
            }

            // 先加 RowsPanel，再加 HeaderPanel (Dock Top 堆叠)
            card.Controls.Add(rowsPanel);
            card.Controls.Add(headerPanel);

            wrapper.Controls.Add(card);
            _container.Controls.Add(wrapper);

            _groupsUI.Add(new GroupUI { Key = groupKey, Input = inputGroup.Inner });
        }

        private Control CreateRow(MonitorItemConfig item, Panel parentContainer)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.White };

            // ID
            var lblId = new Label
            {
                Text = item.Key,
                Location = new Point(X_ID, 14),
                Size = new Size(90, 20),
                AutoEllipsis = true,
                ForeColor = UIColors.TextSub,
                Font = new Font("Segoe UI", 8F)
            };

            // Name
            string defName = LanguageManager.T("Items." + item.Key);
            string valName = string.IsNullOrEmpty(item.UserLabel) ? defName : item.UserLabel;

            // ★ 修复：直接显示，黑色文字
            var inputName = new LiteUnderlineInput(valName);
            inputName.Location = new Point(X_NAME, 8);
            inputName.Size = new Size(120, 28);
            inputName.SetBg(Color.White);
            inputName.Inner.ForeColor = UIColors.TextMain;

            // Short Name
            string defShortKey = "Short." + item.Key;
            string defShort = LanguageManager.T(defShortKey);
            if (defShort.StartsWith("Short.")) defShort = item.Key.Split('.').Last();
            string valShort = string.IsNullOrEmpty(item.TaskbarLabel) ? defShort : item.TaskbarLabel;

            // ★ 修复：直接显示，黑色文字
            var inputShort = new LiteUnderlineInput(valShort);
            inputShort.Location = new Point(X_SHORT, 8);
            inputShort.Size = new Size(60, 28);
            inputShort.SetBg(Color.White);
            inputShort.Inner.ForeColor = UIColors.TextMain;

            var chk1 = new LiteCheck(item.VisibleInPanel) { Location = new Point(X_PANEL + 20, 12) };
            var chk2 = new LiteCheck(item.VisibleInTaskbar) { Location = new Point(X_TASKBAR + 20, 12) };

            var btnUp = new LiteSortBtn("▲") { Location = new Point(X_SORT-18, 10) };
            var btnDown = new LiteSortBtn("▼") { Location = new Point(X_SORT + 17, 10) };
            btnUp.Click += (s, e) => MoveRow(row, -1, parentContainer);
            btnDown.Click += (s, e) => MoveRow(row, 1, parentContainer);

            row.Controls.AddRange(new Control[] { lblId, inputName, inputShort, chk1, chk2, btnUp, btnDown });
            row.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.FromArgb(245, 245, 245)), X_ID, 43, row.Width - 20, 43);

            _rowsUI.Add(new RowUI { Config = item, RowControl = row, InputName = inputName.Inner, InputShort = inputShort.Inner, ChkPanel = chk1, ChkTaskbar = chk2 });
            return row;
        }

        private void MoveRow(Control row, int dir, Panel container)
        {
            int idx = container.Controls.GetChildIndex(row);
            int newIdx = idx - dir;
            if (newIdx >= 0 && newIdx < container.Controls.Count)
                container.Controls.SetChildIndex(row, newIdx);
        }

        private void MoveGroup(Control wrapper, int dir)
        {
            var p = wrapper.Parent;
            int idx = p.Controls.GetChildIndex(wrapper);
            int newIdx = idx - dir;
            if (newIdx >= 0 && newIdx < p.Controls.Count)
                p.Controls.SetChildIndex(wrapper, newIdx);
        }

        public override void Save()
        {
            // 1. 安全检查：如果页面从未加载（_isLoaded为false），控件对象都不存在，
            // 此时千万不能读取，否则会把配置全部覆盖为空！
            if (!_isLoaded) return;
            
            // 初始化字典防止空引用
            if (Config.GroupAliases == null) Config.GroupAliases = new Dictionary<string, string>();
            
            var flatList = new List<MonitorItemConfig>();
            int sortIdx = 0;

            // 遍历容器中的所有分组卡片
            // 注意：这里保持原有的遍历顺序 (Count-1 -> 0)，确保排序逻辑与界面显示一致
            for (int i = _container.Controls.Count - 1; i >= 0; i--)
            {
                if (_container.Controls[i] is Panel wrapper && wrapper.Controls.Count > 0)
                {
                    var card = wrapper.Controls[0] as LiteCard;
                    if (card == null) continue;

                    // LiteCard 内部结构： Controls[0]是RowsPanel, Controls[1]是HeaderPanel (因为是Dock.Top堆叠)
                    var headerPanel = card.Controls.Count > 1 ? card.Controls[1] as Panel : null;
                    var rowsPanel = card.Controls.Count > 0 ? card.Controls[0] as Panel : null;

                    // ====== 1. 保存分组别名 (Group Aliases) ======
                    // 查找标题输入框
                    var gInput = headerPanel?.Controls.OfType<LiteUnderlineInput>().FirstOrDefault()?.Inner;
                    // 通过输入框反向查找对应的 GroupUI 数据
                    var gUI = _groupsUI.FirstOrDefault(u => u.Input == gInput);
                    
                    if (gUI != null)
                    {
                        string val = gUI.Input.Text.Trim();
                        
                        // ★ 修复逻辑：所见即所得。
                        // 只要用户输入了内容，就保存到别名设置中。
                        // 不再判断 "是否等于默认翻译"，防止 Apply 后翻译更新导致的误删除。
                        if (!string.IsNullOrEmpty(val))
                        {
                            Config.GroupAliases[gUI.Key] = val;
                        }
                        else
                        {
                            // 只有当用户显式清空输入框时，才移除别名（恢复默认）
                            if (Config.GroupAliases.ContainsKey(gUI.Key))
                                Config.GroupAliases.Remove(gUI.Key);
                        }
                    }

                    // ====== 2. 保存子项配置 (Rows) ======
                    if (rowsPanel != null)
                    {
                        for (int j = rowsPanel.Controls.Count - 1; j >= 0; j--)
                        {
                            var rowCtrl = rowsPanel.Controls[j];
                            var rUI = _rowsUI.FirstOrDefault(r => r.RowControl == rowCtrl);
                            
                            if (rUI != null)
                            {
                                var item = rUI.Config;

                                // --- 名称 (UserLabel) ---
                                string valName = rUI.InputName.Text.Trim();
                                // ★ 修复逻辑：直接保存内容，不为空则代表用户自定义了名称
                                item.UserLabel = valName; 

                                // --- 简称 (TaskbarLabel) ---
                                string valShort = rUI.InputShort.Text.Trim();
                                // ★ 修复逻辑：直接保存内容
                                item.TaskbarLabel = valShort;

                                // --- 开关状态 ---
                                item.VisibleInPanel = rUI.ChkPanel.Checked;
                                item.VisibleInTaskbar = rUI.ChkTaskbar.Checked;
                                
                                // --- 排序索引 ---
                                // 根据当前 UI 的顺序重新生成 SortIndex
                                item.SortIndex = sortIdx++;
                                
                                flatList.Add(item);
                            }
                        }
                    }
                }
            }
            
            // 更新配置列表
            Config.MonitorItems = flatList;
        }
        private class GroupUI { public string Key; public TextBox Input; }
        private class RowUI { public MonitorItemConfig Config; public Control RowControl; public TextBox InputName; public TextBox InputShort; public CheckBox ChkPanel; public CheckBox ChkTaskbar; }
    }
}