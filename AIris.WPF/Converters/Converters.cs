using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIris.WPF.Converters
{
    /// <summary>布尔取反 → 可见性（设计稿里"启用中/已停用"胶囊二选一显示用）。</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool && (bool)value;
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 元素实际高度 → 正圆药丸圆角（CornerRadius = ActualHeight / 2）。
    ///
    /// 【为什么需要这个转换器：2026-11-23 用户反馈"胶囊太尖锐"的真正根因】
    ///   WPF 的 <c>Border.CornerRadius</c> 与 CSS 的 <c>border-radius</c> 语义不同：
    ///   · CSS 写 <c>border-radius:999px</c> 时，浏览器按"相邻圆角半径之和不得超过边长"
    ///     的规则把所有半径**统一按比例缩小**（这里缩到 高度/2），端头得到**正圆半圆**，
    ///     顶边/底边各留一段水平直边——设计稿的按钮就是这个形状；
    ///   · WPF 遇到超大 CornerRadius 时按**两个方向分别钳制**（水平到 宽/2、垂直到 高/2），
    ///     画出的是**椭圆角**：宽按钮两端被拉成长椭圆、收弧太急，肉眼就是"尖锐/像橄榄球"。
    ///   这不是圆角写小了，而是写大了反而变椭圆——实测同一 32 高按钮：
    ///     r=16（半高）→ 正圆端头；r=999 → 椭圆端头（像素扫描顶部行宽只有 0.23 倍）。
    ///
    /// 【为什么绑 ActualHeight 而不是写死半高值】
    ///   用户要求"基类控件支持各种屏幕比例，复用不变形"。若把 32 高按钮写死 r=16，
    ///   一旦系统字体放大 / 布局撑高（实测字号 26 时按钮自动长到 35），圆角就不再是半高，
    ///   药丸退化成圆角矩形。绑定 ActualHeight/2 后，高度怎么变圆角都跟着变，
    ///   任何 DPI、任何字体、任何窗口比例下都是同一个正圆药丸形状。
    ///
    /// 【用法】
    ///   &lt;Border CornerRadius="{Binding ActualHeight, RelativeSource={RelativeSource Self},
    ///                          Converter={StaticResource PillRadius}}"/&gt;
    ///   资源键 PillRadius 在 Styles/AiStyles.xaml 中注册（全局可用）。
    /// 【边界】ActualHeight 为 0（首次布局前）返回 0 方角，布局完成后立即变半高；
    ///   CornerRadius 不参与 Measure/Arrange，因此不会造成布局循环。
    /// </summary>
    public sealed class PillRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double h = value is double ? (double)value : 0.0;
            double extra = 0.0;
            if (parameter != null)
            {
                double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out extra);
            }
            double r = h <= 0 ? 0.0 : Math.Max(0.0, h / 2.0 + extra);
            return new CornerRadius(r);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
