using System;
using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class ThresholdPage : SettingsPageBase
    {
        private Panel _container;
        
        // Input Cache
        private LiteUnderlineInput _inMaxCpuClk, _inMaxGpuClk, _inMaxCpuPwr, _inMaxGpuPwr;
        private LiteUnderlineInput _inLoadWarn, _inLoadCrit, _inTempWarn, _inTempCrit;
        private LiteUnderlineInput _inDiskWarn, _inDiskCrit, _inUpWarn, _inUpCrit, _inDownWarn, _inDownCrit;
        private LiteUnderlineInput _inDataUpWarn, _inDataUpCrit, _inDataDownWarn, _inDataDownCrit;
        private LiteUnderlineInput _inAlertTemp;
        private LiteCheck _chkAlertTemp;

        // Colors
        private readonly Color C_Warn = Color.FromArgb(255, 180, 0);
        private readonly Color C_Crit = Color.FromArgb(255, 80, 80);
        private readonly Color C_Action = Color.FromArgb(0, 120, 215);

        public ThresholdPage()
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

        private bool _isLoaded = false;

        public override void OnShow()
        {
            if (Config == null || _isLoaded) return;
            
            _container.SuspendLayout();
            _container.Controls.Clear();

            // 1. Max Frequency & Power (Blue headers, Integer inputs)
            CreateMaxLimitCard();

            // 2. General Hardware
            CreateDoubleThresholdCard("⚠️通用硬件 (General Hardware)", 
                new[] { "负载 / Load (%)", "温度 / Temp (°C)" },
                new[] { Config.Thresholds.Load, Config.Thresholds.Temp },
                out _inLoadWarn, out _inLoadCrit, out _inTempWarn, out _inTempCrit);

            // 3. Transfer Speed
            CreateTripleThresholdCard("⚠️传输速率 (Transfer Speed)",
                new[] { "磁盘读写 / Disk (MB/s)", "上传速率 / Net Up (MB/s)", "下载速率 / Net Down (MB/s)" },
                new[] { Config.Thresholds.DiskIOMB, Config.Thresholds.NetUpMB, Config.Thresholds.NetDownMB },
                out _inDiskWarn, out _inDiskCrit, out _inUpWarn, out _inUpCrit, out _inDownWarn, out _inDownCrit);

            // 4. Daily Data
            CreateDoubleThresholdCard("⚠️每日流量 (Daily Data Usage)",
                new[] { "上传总量 / Upload (MB)", "下载总量 / Download (MB)" },
                new[] { Config.Thresholds.DataUpMB, Config.Thresholds.DataDownMB },
                out _inDataUpWarn, out _inDataUpCrit, out _inDataDownWarn, out _inDataDownCrit);

            // 5. Popup Alert
            CreateAlertCard();

            _container.ResumeLayout();
            _isLoaded = true;
        }

        // === Card Builders ===

        private void CreateMaxLimitCard()
        {
            var content = CreateCardWithFlow("最大频率与功耗 (Max Limits)", "CPU (Max)", "GPU (Max)", C_Action, C_Action);
            
            // isIntOnly: true -> 确保显示整数，没有小数点
            AddDualInputRow(content, "最大频率 / Clock (MHz)", 
                Config.RecordedMaxCpuClock, Config.RecordedMaxGpuClock, 
                out _inMaxCpuClk, out _inMaxGpuClk, 
                C_Action, C_Action, isIntOnly: true);

            AddDualInputRow(content, "最大功耗 / Power (W)", 
                Config.RecordedMaxCpuPower, Config.RecordedMaxGpuPower, 
                out _inMaxCpuPwr, out _inMaxGpuPwr,
                C_Action, C_Action, isIntOnly: true);

            AddDescription(content, "⚠️ 为了进度条显示更准确，请填写硬件的实际最大值，如不填，将在高负载时动态学习并更新。");

            AddCardToPage(content.Parent as LiteCard);
        }

        private void CreateAlertCard()
        {
            var content = CreateCardWithFlow("⚠️高温报警弹窗通知 (Popup Alert)");
            
            var row = new Panel { Size = new Size(580, 45), Margin = new Padding(0) };
            
            var lbl = CreateLabel("高温报警触发值 / High Temp Limit (°C)", 20, 12);
            // 报警温度也通常是整数
            _inAlertTemp = CreateNumInput(Config.AlertTempThreshold, C_Crit, isIntOnly: true); 
            _inAlertTemp.Location = new Point(320, 8); 

            _chkAlertTemp = new LiteCheck(Config.AlertTempEnabled) { Text = "开启/Enable", Location = new Point(430, 10) };

            row.Controls.Add(lbl);
            row.Controls.Add(_inAlertTemp);
            row.Controls.Add(_chkAlertTemp);
            
            content.Controls.Add(row);
            AddCardToPage(content.Parent as LiteCard);
        }

        private void CreateDoubleThresholdCard(string title, string[] labels, ValueRange[] values, 
            out LiteUnderlineInput w1, out LiteUnderlineInput c1, 
            out LiteUnderlineInput w2, out LiteUnderlineInput c2)
        {
            var content = CreateCardWithFlow(title, "注意 (Warn)", "重视 (Crit)", C_Warn, C_Crit);

            AddDualInputRow(content, labels[0], values[0].Warn, values[0].Crit, out w1, out c1, C_Warn, C_Crit);
            AddDualInputRow(content, labels[1], values[1].Warn, values[1].Crit, out w2, out c2, C_Warn, C_Crit);
            
            AddCardToPage(content.Parent as LiteCard);
        }

        private void CreateTripleThresholdCard(string title, string[] labels, ValueRange[] values,
            out LiteUnderlineInput w1, out LiteUnderlineInput c1,
            out LiteUnderlineInput w2, out LiteUnderlineInput c2,
            out LiteUnderlineInput w3, out LiteUnderlineInput c3)
        {
            var content = CreateCardWithFlow(title, "注意 (Warn)", "重视 (Crit)", C_Warn, C_Crit);

            AddDualInputRow(content, labels[0], values[0].Warn, values[0].Crit, out w1, out c1, C_Warn, C_Crit);
            AddDualInputRow(content, labels[1], values[1].Warn, values[1].Crit, out w2, out c2, C_Warn, C_Crit);
            AddDualInputRow(content, labels[2], values[2].Warn, values[2].Crit, out w3, out c3, C_Warn, C_Crit);

            AddCardToPage(content.Parent as LiteCard);
        }

        // === Helpers ===

        private FlowLayoutPanel CreateCardWithFlow(string title, string col1 = null, string col2 = null, Color? c1 = null, Color? c2 = null)
        {
            var card = new LiteCard { Dock = DockStyle.Top };
            
            var header = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = UIColors.GroupHeader };
            header.Paint += (s, e) => e.Graphics.DrawLine(new Pen(UIColors.Border), 0, 39, header.Width, 39);
            
            var lbl = new Label { Text = title, Location = new Point(15, 10), AutoSize = true, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = Color.Black };
            header.Controls.Add(lbl);

            if (!string.IsNullOrEmpty(col1))
            {
                var l1 = new Label { Text = col1, Location = new Point(330, 10), AutoSize = true, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = c1 ?? Color.Gray };
                header.Controls.Add(l1);
            }

            if (!string.IsNullOrEmpty(col2))
            {
                var l2 = new Label { Text = col2, Location = new Point(450, 10), AutoSize = true, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = c2 ?? Color.Gray };
                header.Controls.Add(l2);
            }

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
            return flow;
        }

        private void AddDualInputRow(FlowLayoutPanel flow, string label, double val1, double val2, 
            out LiteUnderlineInput in1, out LiteUnderlineInput in2,
            Color color1, Color color2, bool isIntOnly = false)
        {
            var row = new Panel { Size = new Size(580, 45), Margin = new Padding(0) };
            
            var lbl = CreateLabel(label, 20, 10);
            
            in1 = CreateNumInput(val1, color1, isIntOnly); 
            in1.Location = new Point(320, 5); 

            in2 = CreateNumInput(val2, color2, isIntOnly); 
            in2.Location = new Point(440, 5); 

            row.Controls.Add(lbl); row.Controls.Add(in1); row.Controls.Add(in2);
            row.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.WhiteSmoke), 20, 44, row.Width-20, 44);
            
            flow.Controls.Add(row);
        }

        private void AddDescription(FlowLayoutPanel flow, string text)
        {
            // 修改：高度增加到 35，给下方留点白
            var row = new Panel { Size = new Size(580, 30), Margin = new Padding(0) };
            
            // 修改：Y 坐标改为 12，让文字下沉，不要贴着上面的线
            var lbl = new Label { 
                Text = text, 
                Location = new Point(20, 12), 
                AutoSize = true, 
                Font = new Font("Microsoft YaHei UI", 8F), 
                ForeColor = Color.Gray 
            };
            
            row.Controls.Add(lbl);
            flow.Controls.Add(row);
        }

        // ★ 核心修改方法
        private LiteUnderlineInput CreateNumInput(double val, Color? textColor = null, bool isIntOnly = false)
        {
            // 修改点2：如果是整数模式，强制转 int 去掉小数点
            string initText = isIntOnly ? ((int)val).ToString() : val.ToString();

            var input = new LiteUnderlineInput(initText);
            input.Size = new Size(80, 28);
            input.SetBg(Color.White);
            input.Inner.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            
            // 修改点1：文字左右居中
            input.Inner.TextAlign = HorizontalAlignment.Center; 

            if (textColor.HasValue)
            {
                input.Inner.ForeColor = textColor.Value;
            }

            input.Inner.KeyPress += (s, e) => {
                if (char.IsControl(e.KeyChar)) return;
                if (char.IsDigit(e.KeyChar)) return;
                
                // 只有非整数模式下才允许小数点
                if (!isIntOnly && e.KeyChar == '.') 
                {
                    if ((s as TextBox).Text.Contains(".")) e.Handled = true;
                    return;
                }
                e.Handled = true;
            };
            return input;
        }

        private Label CreateLabel(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = new Font("Microsoft YaHei UI", 9F), ForeColor = UIColors.TextMain };
        }

        private void AddCardToPage(LiteCard card)
        {
            var wrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0,0,0,20) };
            wrapper.Controls.Add(card);
            _container.Controls.Add(wrapper);
            _container.Controls.SetChildIndex(wrapper, 0); 
        }

        public override void Save()
        {
            // ★★★ 核心修复：如果页面没打开过，控件都是 null，千万别读！ ★★★
            if (!_isLoaded) return;

            float F(LiteUnderlineInput i) => float.TryParse(i.Inner.Text, out float v) ? v : 0;
            double D(LiteUnderlineInput i) => double.TryParse(i.Inner.Text, out double v) ? v : 0;
            int I(LiteUnderlineInput i) => int.TryParse(i.Inner.Text, out int v) ? v : 0;

            // 1. Limits
            Config.RecordedMaxCpuClock = F(_inMaxCpuClk);
            Config.RecordedMaxGpuClock = F(_inMaxGpuClk);
            Config.RecordedMaxCpuPower = F(_inMaxCpuPwr);
            Config.RecordedMaxGpuPower = F(_inMaxGpuPwr);

            // 2. Hardware
            Config.Thresholds.Load = new ValueRange { Warn = D(_inLoadWarn), Crit = D(_inLoadCrit) };
            Config.Thresholds.Temp = new ValueRange { Warn = D(_inTempWarn), Crit = D(_inTempCrit) };

            // 3. Speed
            Config.Thresholds.DiskIOMB = new ValueRange { Warn = D(_inDiskWarn), Crit = D(_inDiskCrit) };
            Config.Thresholds.NetUpMB = new ValueRange { Warn = D(_inUpWarn), Crit = D(_inUpCrit) };
            Config.Thresholds.NetDownMB = new ValueRange { Warn = D(_inDownWarn), Crit = D(_inDownCrit) };

            // 4. Data
            Config.Thresholds.DataUpMB = new ValueRange { Warn = D(_inDataUpWarn), Crit = D(_inDataUpCrit) };
            Config.Thresholds.DataDownMB = new ValueRange { Warn = D(_inDataDownWarn), Crit = D(_inDataDownCrit) };

            // 5. Alert
            Config.AlertTempThreshold = I(_inAlertTemp);
            Config.AlertTempEnabled = _chkAlertTemp.Checked;
        }
    }
}