using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIris.Core.Detect;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 运行监控台 glue：DataContext 绑 VM + 叠加框绘制（DrawBoxes）。
    /// 叠加框按"画面实际渲染区"比例绘制：Image Stretch=Uniform 且 Mock 帧固定 4:3（1280×960），
    /// 渲染区 = StageGrid 内接矩形；检测框 0~1000 归一化 → 渲染区像素。
    /// </summary>
    public partial class MonitorPage : UserControl
    {
        private MonitorViewModel _vm;

        public MonitorPage()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (_vm != null) _vm.Boxes.CollectionChanged -= Boxes_CollectionChanged;
                _vm = DataContext as MonitorViewModel;
                if (_vm != null) _vm.Boxes.CollectionChanged += Boxes_CollectionChanged;
                if (_vm != null)
                {
                    // FrameImage 变化也重绘（画面比例基准）
                    _vm.PropertyChanged += (s2, e2) => { if (e2.PropertyName == "FrameImage") RedrawBoxes(); };

                    // 运行日志流：新行自动滚到底（View 行为；VM 日志已 Dispatcher 收口在 UI 线程）
                    _vm.Logs.CollectionChanged -= Logs_CollectionChanged;
                    _vm.Logs.CollectionChanged += Logs_CollectionChanged;
                }
                RedrawBoxes();
            };
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

        private void Boxes_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 检测线程事件（已 Dispatcher 收口），直接重绘
            RedrawBoxes();
        }

        private void Stage_SizeChanged(object sender, SizeChangedEventArgs e) { RedrawBoxes(); }

        /// <summary>重绘叠加框：清空 Canvas → 按 VM.Boxes 逐个画（主色描边 + 类目标签，无置信度）。</summary>
        private void RedrawBoxes()
        {
            OverlayCanvas.Children.Clear();
            if (_vm == null || _vm.Boxes.Count == 0) return;

            // 画面渲染区：StageGrid 内接 4:3 矩形（Mock 帧固定 1280×960）
            double gw = StageGrid.ActualWidth, gh = StageGrid.ActualHeight;
            if (gw < 10 || gh < 10) return;
            double rw = Math.Min(gw, gh * 4.0 / 3.0);
            double rh = rw * 3.0 / 4.0;
            double ox = (gw - rw) / 2, oy = (gh - rh) / 2;

            var brush = (Brush)FindResource("ThemePrimary");
            var labelBrush = (Brush)FindResource("ThemePrimary2");

            foreach (var d in _vm.Boxes)
            {
                double x1 = ox + d.X1 * rw / 1000.0, y1 = oy + d.Y1 * rh / 1000.0;
                double w = Math.Max((d.X2 - d.X1) * rw / 1000.0, 4);
                double h = Math.Max((d.Y2 - d.Y1) * rh / 1000.0, 4);

                var box = new Border
                {
                    BorderBrush = brush,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0xF9, 0x73, 0x16))
                };
                Canvas.SetLeft(box, x1);
                Canvas.SetTop(box, y1);
                box.Width = w;
                box.Height = h;

                var label = new TextBlock
                {
                    Text = d.Label,                       // ⚠️ 语义红线：不显示置信度
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = labelBrush,
                    Margin = new Thickness(3, -1, 0, 0)
                };
                box.Child = label;
                OverlayCanvas.Children.Add(box);
            }
        }
    }
}
