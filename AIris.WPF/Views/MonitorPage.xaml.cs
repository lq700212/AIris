using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AIris.Core.Detect;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 运行监控台 glue：DataContext 绑 VM + 叠加框绘制（DrawBoxes）。
    ///
    /// V0.1.12（设计稿 vNext 对齐）：检测框三态上色 —— 框颜色不再恒为主色，而是按
    /// "该框所属工步的状态"着色，操作员一眼看出装配进度与 NG 位置：
    ///   · 橙色实线 = 待装（所属工步尚未开始 / 框无归属）
    ///   · 绿色实线 = 已装（所属工步已完成，或当前工步判定 OK）
    ///   · 黄色虚线 = 等待（当前工步正在攒稳定帧）
    ///   · 红色实线 = NG（当前工步判定 NG）
    /// 归属判定：框 Label ↔ 工步 ExpectedPart 双向包含匹配（Mock/真机 Label 即零件名，
    /// 语义与 JudgeEngine 的类目匹配同源；不改判定结果，只影响显示态）。
    ///
    /// 叠加框按"画面实际渲染区"比例绘制：帧 Stretch=Uniform 且渲染区 = 舞台内接矩形，
    /// 检测框 0~1000 归一化 → 渲染区像素（FrameRect 同源，真机换分辨率零改动）。
    /// </summary>
    public partial class MonitorPage : UserControl
    {
        private MonitorViewModel _vm;

        /// <summary>取像舞台（x:Name 即 StageGrid；Stage 别名指同对象，code-behind 语义直白）。</summary>
        public Kaleidoscope.UI.Views.KxImageStage Stage => StageGrid;

        public MonitorPage()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (_vm != null)
                {
                    _vm.Boxes.CollectionChanged -= Boxes_CollectionChanged;
                    _vm.PropertyChanged -= Vm_PropertyChanged;
                }
                _vm = DataContext as MonitorViewModel;
                if (_vm != null)
                {
                    _vm.Boxes.CollectionChanged += Boxes_CollectionChanged;
                    _vm.PropertyChanged += Vm_PropertyChanged;

                    // 运行日志流：新行自动滚到底（View 行为；VM 日志已 Dispatcher 收口在 UI 线程）
                    _vm.Logs.CollectionChanged -= Logs_CollectionChanged;
                    _vm.Logs.CollectionChanged += Logs_CollectionChanged;
                }
                RedrawBoxes();
            };
        }

        /// <summary>画面帧变化 / 判定变化都重绘（框三态颜色依赖 Verdict）。</summary>
        private void Vm_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "FrameImage" || e.PropertyName == "Verdict") RedrawBoxes();
        }

        /// <summary>日志新行自动滚动到底。必须延后一拍（DispatcherPriority.Background）：
        /// CollectionChanged 可能在布局周期内同步触发，此时直接 ScrollIntoView 会
        /// UpdateLayout 重入递归（实测栈溢出 0xC00000FD 崩进程），踢到布局外再滚。</summary>
        private void Logs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (LogList.Items.Count == 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (LogList.Items.Count == 0) return;
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>NG 详情弹窗（告警卡按钮；走 Host 门面不直接 new Window）。</summary>
        private void NgDetail_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null) _vm.OpenNgDetail();
        }

        private bool _redrawPosted;

        private void Boxes_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // VM 更新框是 Clear + 逐项 Add（两次以上 CollectionChanged），
            // 合并成一次重绘：同一消息循环内只建一遍 UI 元素（旧实现 Clear 与 Add 各重绘一遍）。
            if (_redrawPosted) return;
            _redrawPosted = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _redrawPosted = false;
                RedrawBoxes();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void Stage_SizeChanged(object sender, SizeChangedEventArgs e) { RedrawBoxes(); }

        /// <summary>重绘叠加框（三态着色）。
        /// 每个框 = Grid（Rectangle 描边/填充 + 左上角实底标签），标签永不含置信度（铁律 12）。</summary>
        private void RedrawBoxes()
        {
            var canvas = Stage.Overlay;
            canvas.Children.Clear();
            if (_vm == null || _vm.Boxes.Count == 0) return;

            // 画面渲染区：按帧实际像素宽高求舞台内接矩形（真机换分辨率零改动）
            double gw = StageGrid.ActualWidth, gh = StageGrid.ActualHeight;
            if (gw < 10 || gh < 10) return;
            double rw, rh, ox, oy;
            FrameRect.TryGet(_vm.FrameImage, gw, gh, out ox, out oy, out rw, out rh);

            foreach (var d in _vm.Boxes)
            {
                double x1 = ox + d.X1 * rw / 1000.0, y1 = oy + d.Y1 * rh / 1000.0;
                double w = Math.Max((d.X2 - d.X1) * rw / 1000.0, 6);
                double h = Math.Max((d.Y2 - d.Y1) * rh / 1000.0, 6);

                string state = ResolveBoxState(d);
                Color strokeColor, fillColor;
                if (state == "done") { strokeColor = ColorOf("ThemeOk"); fillColor = Color.FromArgb(0x1A, 0x10, 0xB9, 0x81); }
                else if (state == "ng") { strokeColor = ColorOf("ThemeNg"); fillColor = Color.FromArgb(0x1F, 0xEF, 0x44, 0x44); }
                else if (state == "wait") { strokeColor = ColorOf("ThemeWarn"); fillColor = Color.FromArgb(0x10, 0xF5, 0x9E, 0x0B); }
                else { strokeColor = ColorOf("ThemePrimary"); fillColor = Color.FromArgb(0x22, 0xF9, 0x73, 0x16); }

                var layer = new Grid { Width = w, Height = h };
                var rect = new Rectangle
                {
                    Stroke = new SolidColorBrush(strokeColor),
                    StrokeThickness = state == "wait" ? 1.6 : 1.8,
                    Fill = new SolidColorBrush(fillColor),
                    RadiusX = 5,
                    RadiusY = 5,
                    SnapsToDevicePixels = true
                };
                if (state == "wait") rect.StrokeDashArray = new DoubleCollection(new double[] { 3, 2 });
                layer.Children.Add(rect);

                // 标签：实底药丸（9.5 bold 白字），放在框内左上角（设计稿 .det .lt 口径）
                var tag = new Border
                {
                    Background = new SolidColorBrush(strokeColor),
                    CornerRadius = new CornerRadius(5),
                    Height = 17,
                    Padding = new Thickness(7, 0, 7, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 2, 0, 0)
                };
                tag.Child = new TextBlock
                {
                    Text = d.Label,                       // ⚠️ 语义红线：不显示置信度
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
                layer.Children.Add(tag);

                Canvas.SetLeft(layer, x1);
                Canvas.SetTop(layer, y1);
                canvas.Children.Add(layer);
            }
        }

        /// <summary>框三态归属：按框 Label 匹配工步 ExpectedPart（双向包含，忽略大小写/空白）。
        /// 无归属框：当前 NG 显红，否则橙（待装）；当前工步判定中显黄虚线。</summary>
        private string ResolveBoxState(Detection d)
        {
            var hit = _vm.Steps.FirstOrDefault(s => PartMatches(s.ExpectedPart, d.Label));
            if (hit == null) return _vm.Verdict == "ng" ? "ng" : "pending";
            if (hit.State == "done") return "done";
            if (hit.State == "current")
            {
                if (_vm.Verdict == "ng") return "ng";
                if (_vm.Verdict == "ok") return "done";
                return "wait";
            }
            return "pending";
        }

        private static bool PartMatches(string expected, string label)
        {
            string a = (expected ?? "").Replace(" ", "");
            string b = (label ?? "").Replace(" ", "");
            if (a.Length == 0 || b.Length == 0) return false;
            // 整检类目（"全部零件/全面"）不归属到单个框，避免整检步把全画面染成一色
            if (a.Contains("全部") || a.Contains("齐套")) return false;
            return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0
                || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>取主题色 Color（FindResource 在页面资源链上解析 Theme* 键；缺失回退主色）。</summary>
        private Color ColorOf(string key)
        {
            var brush = TryFindResource(key) as SolidColorBrush;
            return brush != null ? brush.Color : Color.FromRgb(0xF9, 0x73, 0x16);
        }
    }
}
