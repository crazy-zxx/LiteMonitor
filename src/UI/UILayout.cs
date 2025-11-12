using LiteMonitor.src.Core;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LiteMonitor
{
    public class GroupLayoutInfo
    {
        public string GroupName { get; set; }
        public Rectangle Bounds { get; set; }
        public List<MetricItem> Items { get; set; }

        public GroupLayoutInfo(string name, List<MetricItem> items)
        {
            GroupName = name;
            Items = items;
            Bounds = Rectangle.Empty;
        }
    }

    public class UILayout
    {
        private readonly Theme _t;
        public UILayout(Theme t) { _t = t; }

        /// <summary>
        /// 构建界面布局（统一 Network、高度逻辑；为组标题预留独立空间）
        /// </summary>
        public int Build(List<GroupLayoutInfo> groups)
        {
            int x = _t.Layout.Padding;
            int y = _t.Layout.Padding;

            // === 1️⃣ 主标题空间（根据语言配置判断是否显示） ===
            string titleText = LanguageManager.T("Title");
            if (!string.IsNullOrEmpty(titleText) && titleText != "Title")
                y += _t.Layout.RowHeight + _t.Layout.Padding;



            int w = _t.Layout.Width - _t.Layout.Padding * 2;
            int rowH = _t.Layout.RowHeight;

            foreach (var g in groups)
            {
                // === 2️⃣ 测量组标题高度 ===
                int headerH = TextRenderer.MeasureText(
                    LanguageManager.T($"Groups.{g.GroupName}"), _t.FontGroup).Height;
                headerH = (int)System.Math.Ceiling(headerH * 1.15);

                // === 3️⃣ 内容区高度 ===
                int innerHeight;
                if (g.GroupName.Equals("NET", StringComparison.OrdinalIgnoreCase) ||
                    g.GroupName.Equals("DISK", StringComparison.OrdinalIgnoreCase))
                {
                    // 双行：一行主值 + 一行说明/单位，附加行距 = 行高 * 0.35（你可再调）
                    int twoLineH = rowH + (int)Math.Ceiling(rowH * 0.1);
                    innerHeight = twoLineH + _t.Layout.ItemGap; // 单条 + 行间距
                }
                else
                {
                    innerHeight = g.Items.Count * rowH + (g.Items.Count - 1) * _t.Layout.ItemGap;
                }

                // === 4️⃣ 组块高度 ===
                int groupHeight = _t.Layout.GroupPadding * 2
                                + innerHeight
                                + _t.Layout.GroupBottom;

                // === 5️⃣ 分配块区域 ===
                g.Bounds = new Rectangle(x, y, w, groupHeight);

                // === 6️⃣ 子项布局 ===
                int itemY = y + _t.Layout.GroupPadding;
                foreach (var it in g.Items)
                {
                    it.Bounds = new Rectangle(x, itemY, w, rowH);
                    itemY += rowH + _t.Layout.ItemGap;
                }

                // === 7️⃣ 下一个组起点 ===
                y += groupHeight + _t.Layout.GroupSpacing + _t.Layout.GroupBottom;
            }

            // === 8️⃣ 最终窗口高度 ===
            // 返回内容区总高度：最后一个组块底部到当前 y 的距离，扣掉末尾追加的 GroupSpacing/Bottom
            int contentHeight = groups.Count > 0 ? (groups[^1].Bounds.Bottom + _t.Layout.Padding) : _t.Layout.Width;
            return contentHeight;
        }
    }
}
