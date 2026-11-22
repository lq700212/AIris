using System;
using System.Windows;
using AIris.Core.Config;

namespace AIris.WPF.Services
{
    /// <summary>
    /// 主题热切换（口径对位 HJVision ThemeService）：
    /// 切换 = Application.Resources.MergedDictionaries 整字典替换（ThemeLight ↔ ThemeDark），
    /// 开关持久化 MainSetting.ini [Theme] IsDark（ON/OFF 口径）。
    /// 线程约束：必须在 UI 线程调用（换字典碰 Application.Resources）。
    /// </summary>
    public static class ThemeService
    {
        private static IniFile _store;

        /// <summary>启动链早期调用：绑定 INI 存储并按存档恢复明暗。</summary>
        public static void AttachStore(IniFile ini)
        {
            _store = ini;
            SetDark(ini != null && ini.ReadFlag("Theme", "IsDark", false));
        }

        public static bool IsDark { get; private set; }

        /// <summary>切换到深/浅主题并写 INI。</summary>
        public static void SetDark(bool dark)
        {
            IsDark = dark;
            var dicts = Application.Current.Resources.MergedDictionaries;
            // 定位当前宿主主题字典（Source 以 ThemeLight/ThemeDark 结尾的那条），整条替换
            for (int i = 0; i < dicts.Count; i++)
            {
                var src = dicts[i].Source;
                if (src != null && (src.OriginalString.Contains("/ThemeLight.xaml") || src.OriginalString.Contains("/ThemeDark.xaml")))
                {
                    var next = new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/AIris.WPF;component/Styles/" + (dark ? "ThemeDark.xaml" : "ThemeLight.xaml"))
                    };
                    dicts[i] = next;
                    break;
                }
            }
            if (_store != null) _store.WriteFlag("Theme", "IsDark", dark);
        }

        public static void Toggle() { SetDark(!IsDark); }
    }
}
