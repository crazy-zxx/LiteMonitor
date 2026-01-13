using LiteMonitor.src.Core;

namespace LiteMonitor
{
    /// <summary>
    /// 定义该指标项的渲染风格
    /// </summary>
    public enum MetricRenderStyle
    {
        StandardBar, // 标准：左标签 + 右数值 + 底部进度条 (CPU/MEM/GPU)
        TwoColumn,   // 双列：居中标签 + 居中数值 (NET/DISK)
        TextOnly     // [新增] 纯文本模式：左标签 + 右文本 (无进度条，用于 NET.IP)
    }

    public class MetricItem
    {
        private string _key = "";
        
        // [保留优化] 强制驻留字符串
        public string Key 
        { 
            get => _key;
            set => _key = UIUtils.Intern(value); 
        }

        private string _label = "";
        public string Label 
        {
            get => _label;
            set => _label = UIUtils.Intern(value);
        }
        
        // [保留优化] 缓存短标签
        private string _shortLabel = "";
        public string ShortLabel 
        {
            get => _shortLabel;
            set => _shortLabel = UIUtils.Intern(value);
        }
        
        public float? Value { get; set; } = null;
        public float DisplayValue { get; set; } = 0f;

        // [新增] 文本值覆盖 (用于 IP 显示)
        public string TextValue { get; set; } = null;

        // =============================
        // [保留优化] 缓存字段
        // =============================
        private float _cachedDisplayValue = -99999f; // 上一次格式化时的数值
        private string _cachedNormalText = "";       // 缓存竖屏文本
        private string _cachedHorizontalText = "";   // 缓存横屏/任务栏文本
        public int CachedColorState { get; private set; } = 0;// [新增] 缓存颜色状态    
        public double CachedPercent { get; private set; } = 0.0;// [新增] 缓存进度条百分比 (0.0 ~ 1.0)

        // [新增] 面向对象的方法：给我主题，我给你我的颜色
        // 渲染器调用这个方法，读起来非常像自然语言
        public Color GetTextColor(Theme t)
        {
            return UIUtils.GetStateColor(CachedColorState, t, true);
        }

        /// <summary>
        /// 获取格式化后的文本（带缓存机制）
        /// </summary>
        /// <param name="isHorizontal">是否为横屏/任务栏模式（需要极简格式）</param>
        public string GetFormattedText(bool isHorizontal)
        {
            // [新增] 如果有强制文本值，直接返回 (支持 IP 显示)
            if (TextValue != null) return TextValue;

            // 仅在数值变化时触发 (Tick 级更新)
            if (Math.Abs(DisplayValue - _cachedDisplayValue) > 0.05f)
            {
                _cachedDisplayValue = DisplayValue;
                _cachedNormalText = UIUtils.FormatValue(Key, DisplayValue);
                _cachedHorizontalText = UIUtils.FormatHorizontalValue(_cachedNormalText);

                // 1. 计算并缓存颜色状态
                CachedColorState = UIUtils.GetColorResult(Key, DisplayValue);

                // 2. ★★★ 计算并缓存进度条百分比 ★★★
                // 这样 DrawBar 就不用再做 IndexOf 字符串匹配了
                CachedPercent = UIUtils.GetUnifiedPercent(Key, DisplayValue);
            }
            return isHorizontal ? _cachedHorizontalText : _cachedNormalText;
        }

        // =============================
        // 布局数据 (由 UILayout 计算填充)
        // =============================
        public MetricRenderStyle Style { get; set; } = MetricRenderStyle.StandardBar;
        public Rectangle Bounds { get; set; } = Rectangle.Empty;

        public Rectangle LabelRect;   
        public Rectangle ValueRect;   
        public Rectangle BarRect;     
        public Rectangle BackRect;    

        /// <summary>
        /// 平滑更新显示值
        /// </summary>
        public void TickSmooth(double speed)
        {
            if (!Value.HasValue) return;
            float target = Value.Value;
            float diff = Math.Abs(target - DisplayValue);

            // 忽略极小的变化，防止动画抖动
            if (diff < 0.05f) return;

            // 距离过大或速度过快时直接跳转
            if (diff > 15f || speed >= 0.9)
                DisplayValue = target;
            else
                DisplayValue += (float)((target - DisplayValue) * speed);
        }
    }
}