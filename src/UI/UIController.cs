using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LiteMonitor
{
    public class UIController : IDisposable
    {
        private readonly Settings _cfg;
        private readonly Form _form;
        private readonly HardwareMonitor _mon;
        private readonly System.Windows.Forms.Timer _timer;

        private UILayout _layout;
        private bool _layoutDirty = true;
        private bool _dragging = false;

        // [新增] 缓存上一次的 IP，避免重复刷新 UI
        private string _lastIP = "init";

        private List<GroupLayoutInfo> _groups = new();
        private List<Column> _hxColsHorizontal = new();
        private List<Column> _hxColsTaskbar = new();
        private HorizontalLayout? _hxLayout;
        public MainForm MainForm => (MainForm)_form;

        public List<Column> GetTaskbarColumns() => _hxColsTaskbar;

        public UIController(Settings cfg, Form form)
        {
            _cfg = cfg;
            _form = form;
            _mon = new HardwareMonitor(cfg);
            _mon.OnValuesUpdated += () => _form.Invalidate();

            _layout = new UILayout(ThemeManager.Current);

            _timer = new System.Windows.Forms.Timer { Interval = Math.Max(80, _cfg.RefreshMs) };
            _timer.Tick += (_, __) => Tick();
            _timer.Start();

            ApplyTheme(_cfg.Skin);
        }

        public float GetCurrentDpiScale()
        {
            using (Graphics g = _form.CreateGraphics())
            {
                return g.DpiX / 96f;
            }
        }

        public void ApplyTheme(string name)
        {
            // 1. 先保留旧主题的引用 (为了稍后释放)
            var oldTheme = ThemeManager.Current;

            // 2. 清理全局画刷缓存 (这不会影响 ThemeManager 的字体了，因为解耦了)
            UIRenderer.ClearCache();
            UIUtils.ClearBrushCache();

            // 3. 加载新主题 (Current 指向新对象，包含全新的字体)
            ThemeManager.Load(name);
            var t = ThemeManager.Current;

            // 4. 安全释放旧主题的字体
            // 此时 Current 已经是新主题了，Paint 事件只会用新字体，所以释放旧的是安全的
            if (oldTheme != null && oldTheme != t)
            {
                oldTheme.DisposeFonts();
            }

            // ... 后续缩放逻辑保持不变 ...
            float dpiScale = GetCurrentDpiScale();   
            float userScale = (float)_cfg.UIScale;    
            float finalScale = dpiScale * userScale;

            t.Scale(dpiScale, userScale); // Scale 内部现在会自动清理旧缩放字体

            // ... 边距修复逻辑 ...
            if (!_cfg.HorizontalMode)
            {
                t.Layout.Width = (int)(_cfg.PanelWidth * finalScale);
                _form.ClientSize = new Size(t.Layout.Width, _form.ClientSize.Height);
            }

            TaskbarRenderer.ReloadStyle(_cfg);

            _layout = new UILayout(t);
            _hxLayout = null;

            BuildMetrics();
            BuildHorizontalColumns();
            _layoutDirty = true;

            _form.BackColor = ThemeManager.ParseColor(t.Color.Background);

            _timer.Interval = Math.Max(80, _cfg.RefreshMs);
            _form.Invalidate();
            _form.Update();
        }

        public void RebuildLayout()
        {
            BuildMetrics();
            BuildHorizontalColumns(); 
            _layoutDirty = true;
            _form.Invalidate();
            _form.Update();
        }

        public void SetDragging(bool dragging) => _dragging = dragging;

        public void Render(Graphics g)
        {
            var t = ThemeManager.Current;
            _layout ??= new UILayout(t);

            // === 横屏模式 ===
            if (_cfg.HorizontalMode)
            {
                _hxLayout ??= new HorizontalLayout(t, _form.Width, LayoutMode.Horizontal);
                
                if (_layoutDirty)
                {
                    int h = _hxLayout.Build(_hxColsHorizontal);
                    // 同样建议横屏模式也使用 ClientSize
                    // _form.Width = ... 
                    // _form.Height = h;
                    _form.ClientSize = new Size(_hxLayout.PanelWidth, h);
                    _layoutDirty = false;
                }
                HorizontalRenderer.Render(g, t, _hxColsHorizontal, _hxLayout.PanelWidth);
                return;
            }

            // === 竖屏模式 ===
            if (_layoutDirty)
            {
                int h = _layout.Build(_groups);
                // [修复2补充] 设置高度时也使用 ClientSize，确保高度精准
                _form.ClientSize = new Size(_form.ClientSize.Width, h);
                _layoutDirty = false;
            }

            UIRenderer.Render(g, _groups, t);
        }

        private bool _busy = false;

        private async void Tick()
        {
            if (_dragging || _busy) return;
            _busy = true;

            try
            {
                await System.Threading.Tasks.Task.Run(() => _mon.UpdateAll());

                // ======================================================
                // [优化] 只有当 IP 开启显示时，才去获取 IP
                // ======================================================
                bool showIpPanel = _cfg.MonitorItems.Any(x => x.Key == "NET.IP" && x.VisibleInPanel);
                bool showIpTaskbar = _cfg.MonitorItems.Any(x => x.Key == "NET.IP" && x.VisibleInTaskbar);
                
                string currentIP = "";
                
                // 如果任意一处需要显示 IP，才调用底层的 GetNetworkIP (底层已有缓存，性能无忧)
                if (showIpPanel || showIpTaskbar)
                {
                    currentIP = _mon.GetNetworkIP();

                    // 检测 IP 变化 (仅在变化时触发重排)
                    if (currentIP != _lastIP)
                    {
                        _lastIP = currentIP;
                        
                        // 1. 更新竖屏标题 (仅当面板显示开启时)
                        if (showIpPanel)
                        {
                            var netGroup = _groups.FirstOrDefault(g => g.GroupName == "NET" || g.GroupName == "DATA");
                            if (netGroup != null)
                            {
                                string baseTitle = LanguageManager.T(UIUtils.Intern("Groups." + netGroup.GroupName));
                                netGroup.Label = !string.IsNullOrEmpty(currentIP) ? $"{baseTitle}  {currentIP}" : baseTitle;
                            }
                        }

                        // 2. 标记重排 (横屏/任务栏列宽需要重算)
                        _layoutDirty = true; 
                    }
                }
                // ======================================================

                // ① 更新竖屏 items
                foreach (var g in _groups)
                    foreach (var it in g.Items)
                    {
                        it.Value = _mon.Get(it.Key);
                        it.TickSmooth(_cfg.AnimationSpeed);
                    }

                // ② 更新横版 / 任务栏 (清理了冗余代码)
                void UpdateCol(Column col)
                {
                    void UpdateItem(MetricItem it) 
                    {
                        if (it == null) return;
                        if (it.Key == "NET.IP") 
                        {
                            // 只有显示开启时才赋值，否则为空
                            it.TextValue = currentIP; 
                        }
                        else 
                        {
                            it.Value = _mon.Get(it.Key);
                            it.TickSmooth(_cfg.AnimationSpeed);
                        }
                    }
                    UpdateItem(col.Top);
                    UpdateItem(col.Bottom);
                }
                
                foreach (var col in _hxColsHorizontal) UpdateCol(col);
                foreach (var col in _hxColsTaskbar) UpdateCol(col);
 
                CheckTemperatureAlert();
                _form.Invalidate();   
            }
            finally
            {
                _busy = false;
            }
        }

        private void BuildMetrics()
        {
            _groups = new List<GroupLayoutInfo>();

            var activeItems = _cfg.MonitorItems
                .Where(x => x.VisibleInPanel)
                .OrderBy(x => x.SortIndex)
                .ToList();

            if (activeItems.Count == 0) return;

            // [新增] 预先获取 IP 状态
            bool showIp = _cfg.MonitorItems.Any(x => x.Key == "NET.IP" && x.VisibleInPanel);
            string ipSuffix = showIp ? _mon.GetNetworkIP() : "";

            string currentGroupKey = "";
            List<MetricItem> currentGroupList = new List<MetricItem>();

            foreach (var cfgItem in activeItems)
            {
                // [新增] ★★★ 拦截 NET.IP ★★★
                // 竖屏模式下，不创建 IP 的实体 Item，只把它作为标题后缀
                if (cfgItem.Key == "NET.IP") continue;

                string groupKey = cfgItem.UIGroup;

                if (groupKey != currentGroupKey && currentGroupList.Count > 0)
                {
                    var gr = new GroupLayoutInfo(currentGroupKey, currentGroupList);
                    string gName = LanguageManager.T(UIUtils.Intern("Groups." + currentGroupKey));
                    if (_cfg.GroupAliases.ContainsKey(currentGroupKey)) gName = _cfg.GroupAliases[currentGroupKey];
                    
                    // [新增] 动态拼接 IP 到上一组的标题 (如果是 NET 组)
                    if ((currentGroupKey == "NET") && !string.IsNullOrEmpty(ipSuffix))
                    {
                        gName += $" {ipSuffix}";
                    }

                    gr.Label = gName;
                    _groups.Add(gr);
                    currentGroupList = new List<MetricItem>();
                }

                currentGroupKey = groupKey;

                string label = LanguageManager.T(UIUtils.Intern("Items." + cfgItem.Key));
                var item = new MetricItem 
                { 
                    Key = cfgItem.Key, 
                    Label = label 
                };
                item.ShortLabel = LanguageManager.T(UIUtils.Intern("Short." + cfgItem.Key));
                
                float? val = _mon.Get(item.Key);
                item.Value = val;
                if (val.HasValue) item.DisplayValue = val.Value;

                currentGroupList.Add(item);
            }

            if (currentGroupList.Count > 0)
            {
                var gr = new GroupLayoutInfo(currentGroupKey, currentGroupList);
                string gName = LanguageManager.T(UIUtils.Intern("Groups." + currentGroupKey));
                 if (_cfg.GroupAliases.ContainsKey(currentGroupKey)) gName = _cfg.GroupAliases[currentGroupKey];
                
                // [新增] 同样处理最后一组
                if ((currentGroupKey == "NET") && !string.IsNullOrEmpty(ipSuffix))
                {
                    gName += $" {ipSuffix}";
                }

                gr.Label = gName;
                _groups.Add(gr);
            }
        }

        private void BuildHorizontalColumns()
        {
            _hxColsHorizontal = BuildColumnsCore(forTaskbar: false);
            _hxColsTaskbar = BuildColumnsCore(forTaskbar: true);
        }

        private List<Column> BuildColumnsCore(bool forTaskbar)
        {
            var cols = new List<Column>();

            // 1. 筛选
            var query = _cfg.MonitorItems
                .Where(x => forTaskbar ? x.VisibleInTaskbar : x.VisibleInPanel);

            // 2. 排序
            if (forTaskbar || _cfg.HorizontalFollowsTaskbar)
                query = query.OrderBy(x => x.TaskbarSortIndex);
            else
                query = query.OrderBy(x => x.SortIndex);

            var items = query.ToList();
            var validItems = new List<MonitorItemConfig>();

            // [新增] 二次过滤：横条模式不显示 IP
            foreach (var item in items)
            {
                // 如果不是任务栏模式（即横屏桌面模式），且是 IP，则跳过
                if (!forTaskbar && item.Key == "NET.IP") continue;
                
                validItems.Add(item);
            }

            bool singleLine = forTaskbar && _cfg.TaskbarSingleLine;
            int step = singleLine ? 1 : 2;

            for (int i = 0; i < validItems.Count; i += step)
            {
                var col = new Column();
                col.Top = CreateMetric(validItems[i]);

                if (!singleLine && i + 1 < validItems.Count)
                {
                    col.Bottom = CreateMetric(validItems[i + 1]);
                }
                cols.Add(col);
            }

            return cols;
        }

        private MetricItem CreateMetric(MonitorItemConfig cfg)
        {
            var item = new MetricItem 
            { 
                Key = cfg.Key 
            };
            
            // [新增] 针对 NET.IP 特殊处理
            if (cfg.Key == "NET.IP")
            {
                item.Label = " ";      // 抹除长标签
                item.ShortLabel = " "; // 抹除短标签
                // 立即填充值，防止刚启动时为空
                item.TextValue = _mon.GetNetworkIP(); 
                item.Style = MetricRenderStyle.TextOnly;
            }
            else
            {
                item.Label = LanguageManager.T(UIUtils.Intern("Items." + cfg.Key));
                item.ShortLabel = LanguageManager.T(UIUtils.Intern("Short." + cfg.Key));
                InitMetricValue(item);
            }
            
            return item;
        }

        private void InitMetricValue(MetricItem? item)
        {
            if (item == null) return;
            float? val = _mon.Get(item.Key);
            item.Value = val;
            if (val.HasValue) item.DisplayValue = val.Value;
        }
        
        private void CheckTemperatureAlert()
        {
            if (!_cfg.AlertTempEnabled) return;
            if ((DateTime.Now - _cfg.LastAlertTime).TotalMinutes < 3) return;

            int globalThreshold = _cfg.AlertTempThreshold; 
            int diskThreshold = Math.Min(globalThreshold - 20, 60); 

            List<string> alertLines = new List<string>();
            string alertTitle = LanguageManager.T("Menu.AlertTemp"); 

            float? cpuTemp = _mon.Get("CPU.Temp");
            if (cpuTemp.HasValue && cpuTemp.Value >= globalThreshold)
                alertLines.Add($"CPU {alertTitle}: 🔥{cpuTemp:F0}°C");

            float? gpuTemp = _mon.Get("GPU.Temp");
            if (gpuTemp.HasValue && gpuTemp.Value >= globalThreshold)
                alertLines.Add($"GPU {alertTitle}: 🔥{gpuTemp:F0}°C");

            float? moboTemp = _mon.Get("MOBO.Temp");
            if (moboTemp.HasValue && moboTemp.Value >= globalThreshold)
                alertLines.Add($"MOBO {alertTitle}: 🔥{moboTemp:F0}°C");

            float? diskTemp = _mon.Get("DISK.Temp");
            if (diskTemp.HasValue && diskTemp.Value >= diskThreshold)
                alertLines.Add($"DISK {alertTitle}: 🔥{diskTemp:F0}°C (>{diskThreshold}°C)");

            if (alertLines.Count > 0)
            {
                string thresholdText = (alertLines.Count == 1 && alertLines[0].StartsWith("DISK")) 
                    ? $"(>{diskThreshold}°C)" 
                    : $"(>{globalThreshold}°C)";

                alertTitle += $" {thresholdText}";
                string bodyText = string.Join("\n", alertLines);
                
                ((MainForm)_form).ShowNotification(alertTitle, bodyText, ToolTipIcon.Warning);
                _cfg.LastAlertTime = DateTime.Now;
            }
        }
        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            _mon.Dispose();
        }
    }
}