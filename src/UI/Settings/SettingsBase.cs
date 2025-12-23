using System.Windows.Forms;
using System.Drawing;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.SettingsPage
{
    public interface ISettingsPage
    {
        void Save();
        void OnShow();
    }

    public class SettingsPageBase : UserControl, ISettingsPage
    {
        protected Settings Config;
        
        // 定义全局通用的淡灰背景色，方便统一修改
        public static readonly Color GlobalBackColor = Color.FromArgb(249, 249, 249); 

        public SettingsPageBase() 
        {
            // 改为淡灰，不再是刺眼的纯白
            this.BackColor = GlobalBackColor; 
            this.Dock = DockStyle.Fill;
        }

        public void SetConfig(Settings cfg)
        {
            Config = cfg;
        }

        public virtual void Save() { }
        public virtual void OnShow() { }
    }
}