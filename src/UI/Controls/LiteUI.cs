using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteMonitor.src.UI.Controls
{
    public static class UIColors
    {
        public static Color MainBg = Color.FromArgb(243, 243, 243);    // 窗体背景
        public static Color SidebarBg = Color.FromArgb(240, 240, 240); // 侧边栏背景
        public static Color CardBg = Color.White;
        public static Color Border = Color.FromArgb(220, 220, 220);
        public static Color Primary = Color.FromArgb(0, 120, 215);
        public static Color TextMain = Color.FromArgb(32, 32, 32);
        public static Color TextSub = Color.FromArgb(120, 120, 120);
        public static Color GroupHeader = Color.FromArgb(248, 249, 250); 
        
        // ★ Win11 风格：选中态是浅灰，不是纯白
        public static Color NavSelected = Color.FromArgb(230, 230, 230); 
        public static Color NavHover = Color.FromArgb(235, 235, 235);
    }

    // 1. 输入框 (宽度缩减，防止遮挡)
    public class LiteUnderlineInput : Panel
    {
        public TextBox Inner;
        public LiteUnderlineInput(string text)
        {
            this.Size = new Size(110, 26); // ★ 宽度从 140 减小到 110
            this.BackColor = Color.Transparent;
            this.Padding = new Padding(0, 5, 0, 2); 

            Inner = new TextBox
            {
                Text = text, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
                BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 9F), ForeColor = UIColors.TextMain
            };
            Inner.Enter += (s, e) => this.Invalidate();
            Inner.Leave += (s, e) => this.Invalidate();
            this.Controls.Add(Inner);
            this.Click += (s, e) => Inner.Focus();
        }
        public void SetBg(Color c) { Inner.BackColor = c; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Inner.Focused ? UIColors.Primary : Color.LightGray;
            int h = Inner.Focused ? 2 : 1;
            using (var b = new SolidBrush(c)) e.Graphics.FillRectangle(b, 0, Height - h, Width, h);
        }
    }

    // 2. 侧边栏按钮 (Win11 风格)
    public class LiteNavBtn : Button
    {
        private bool _isActive;
        public bool IsActive 
        {
            get => _isActive;
            set { _isActive = value; Invalidate(); } // 简化刷新逻辑
        }

        public LiteNavBtn(string text)
        {
            Text = "  " + text;
            Size = new Size(150, 40);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            TextAlign = ContentAlignment.MiddleLeft; Font = new Font("Microsoft YaHei UI", 10F);
            Cursor = Cursors.Hand; 
            Margin = new Padding(5, 2, 5, 2); // 上下间距
            BackColor = UIColors.SidebarBg;
            ForeColor = UIColors.TextMain;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 1. 绘制背景 (选中浅灰 / 悬停微灰 / 默认透明)
            Color bg = _isActive ? UIColors.NavSelected : 
                       (ClientRectangle.Contains(PointToClient(Cursor.Position)) ? UIColors.NavHover : UIColors.SidebarBg);
            
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, ClientRectangle);

            // 2. 选中蓝条 (左侧)
            if (_isActive)
            {
                using (var b = new SolidBrush(UIColors.Primary))
                    e.Graphics.FillRectangle(b, 0, 8, 3, Height - 16);
                Font = new Font(Font, FontStyle.Bold);
            }
            else
            {
                Font = new Font(Font, FontStyle.Regular);
            }

            // 3. 绘制文字
            TextRenderer.DrawText(e.Graphics, Text, Font, new Point(12, 9), UIColors.TextMain);
        }
        
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    }

    // 3. 排序按钮
    public class LiteSortBtn : Button
    {
        public LiteSortBtn(string txt)
        {
            Text = txt; Size = new Size(24, 24); FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0; BackColor = Color.FromArgb(245, 245, 245);
            ForeColor = Color.DimGray; Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold); Margin = new Padding(0);
        }
    }

    // 4. 卡片容器
    public class LiteCard : Panel
    {
        public LiteCard() { BackColor = UIColors.CardBg; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top; Padding = new Padding(1); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(UIColors.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }

    public class LiteCheck : CheckBox { public LiteCheck(bool val) { Checked=val; AutoSize=true; Cursor=Cursors.Hand; Text=""; Padding=new Padding(2); } }
    public class LiteButton : Button { public LiteButton(string t, bool p) { Text=t; Size=new Size(80,32); FlatStyle=FlatStyle.Flat; Cursor=Cursors.Hand; Font=new Font("Segoe UI",9F); if(p){BackColor=UIColors.Primary;ForeColor=Color.White;FlatAppearance.BorderSize=0;} else{BackColor=Color.White;ForeColor=UIColors.TextMain;FlatAppearance.BorderColor=UIColors.Border;} } }
}