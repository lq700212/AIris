using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 配方编辑器 glue：DataContext 绑 VM + ROI 标画交互（V0.1.7 大改）。
    ///
    /// 【V0.1.7 从"数值标画"升级为"取像上拖拽标画"】
    ///  底图：库 KxImageStage（V2.3.7 Overlay 可交互口径）——与监控台同一取像控件，
    ///  启动取像 = RunMonitor.StartPreview 只开相机不做检测（不动监控台运行状态）。
    ///  交互三件（全在 RoiStage.Overlay 上，命中优先级：手柄 > 已有框 > 空白）：
    ///   ①拖已有框 = 移动（起点算好各框归一化坐标，Δpx→Δ归一化实时回写 VM）
    ///   ②四角手柄 = 缩放（拖哪角哪角动，对角锚定）
    ///   ③空白按下拖拽 = 画新框（橡皮筋预览，松手≥阈值落 ROI，过小放弃防误触）
    ///  坐标口径：归一化 0~1000 ↔ 画面渲染区像素（Uniform letterbox 内接矩形，
    ///  按帧实际像素宽高换算——不再硬编码 4:3，真机相机换分辨率零改动）。
    ///  数值联动：RoiVm.X/Y/W/H 的 INPC + SelectedRoi 引用 + 集合变化三路都重绘
    ///  （修复旧版"数值框改动画布不刷新"缺口）。
    /// </summary>
    public partial class RecipeEditorPage : UserControl
    {
        private RecipeEditorViewModel _vm;

        // —— 拖拽状态机（三态互斥；_dragMode=null 表示空闲） ——
        private enum DragMode { None, Move, Resize, DrawNew }
        private DragMode _dragMode = DragMode.None;
        private RoiVm _dragRoi;             // Move/Resize 的目标框
        private int _dragCorner;            // Resize 手柄角标（0=左上 1=右上 2=左下 3=右下）
        private Point _dragStart;           // 按下点（舞台像素坐标）
        private int[] _dragOrig = new int[4];   // 按下时的 X/Y/W/H 快照（Move/Resize 基准）
        private Border _rubber;             // DrawNew 橡皮筋预览框

        public RecipeEditorPage()
        {
            InitializeComponent();
            // 命中垫兜底：KxImageStage 是无 Background 的 Grid，空白处命中会穿透到外层
            // Border（事件根本不到 Stage）——显式给透明背景保证整面可命中（空白画新框的前提）
            RoiStage.Background = Brushes.Transparent;
            // 空白按下走 PreviewMouseDown（隧道事件）：比冒泡 MouseLeftButtonDown 更早到达，
            // 不会被框/手柄的 e.Handled 拦掉——处理器内部按"命中是否 ROI 元素"自行分岔
            RoiStage.PreviewMouseDown += RoiStage_MouseDown;
            RoiStage.PreviewMouseUp += RoiStage_MouseUp;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= Vm_PropertyChanged;
                _vm.Rois.CollectionChanged -= Rois_CollectionChanged;
                DetachRoiHook(_vm.SelectedRoi);
            }
            _vm = DataContext as RecipeEditorViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += Vm_PropertyChanged;
                _vm.Rois.CollectionChanged += Rois_CollectionChanged;
                HookRoi(_vm.SelectedRoi);
                RedrawRois();
            }
        }

        // ============ 联动订阅（数值双向同步的三路入口） ============

        private void Rois_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) { RedrawRois(); }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedRoi")
            {
                DetachRoiHook(_lastHooked);     // 旧选中行数值监听摘除
                HookRoi(_vm != null ? _vm.SelectedRoi : null);
                RedrawRois();
            }
        }

        private RoiVm _lastHooked;
        /// <summary>挂上选中 ROI 的属性监听：数值框改 X/Y/W/H → 画布即时跟随（旧版缺口）。</summary>
        private void HookRoi(RoiVm roi)
        {
            DetachRoiHook(_lastHooked);
            if (roi == null) return;
            roi.PropertyChanged += RoiNumbersChanged;
            _lastHooked = roi;
        }

        private void DetachRoiHook(RoiVm roi)
        {
            if (roi == null) return;
            roi.PropertyChanged -= RoiNumbersChanged;
            if (ReferenceEquals(_lastHooked, roi)) _lastHooked = null;
        }

        private void RoiNumbersChanged(object sender, PropertyChangedEventArgs e) { RedrawRois(); }

        private void RoiStage_SizeChanged(object sender, SizeChangedEventArgs e) { RedrawRois(); }

        // ============ 坐标换算（归一化 0~1000 ↔ 舞台像素） ============

        /// <summary>画面渲染区：帧非空按帧实际像素宽高求舞台内接矩形（Uniform 口径）；
        /// 帧空回退整舞台（无底图也允许画框，坐标系即舞台本身）。换算抽 FrameRect 共用。</summary>
        private bool TryGetFrameRect(out double ox, out double oy, out double rw, out double rh)
        {
            ox = oy = rw = rh = 0;
            double gw = RoiStage.ActualWidth, gh = RoiStage.ActualHeight;
            if (gw < 10 || gh < 10) return false;
            FrameRect.TryGet(_vm != null ? _vm.FrameImage : null, gw, gh, out ox, out oy, out rw, out rh);
            return true;
        }

        /// <summary>舞台像素 → 归一化 0~1000（钳位：拖到头得贴边矩形）。</summary>
        private bool TryPointToNorm(Point p, out int nx, out int ny)
        {
            nx = ny = 0;
            double ox, oy, rw, rh;
            if (!TryGetFrameRect(out ox, out oy, out rw, out rh)) return false;
            nx = ClampNorm((p.X - ox) * 1000.0 / rw);
            ny = ClampNorm((p.Y - oy) * 1000.0 / rh);
            return true;
        }

        /// <summary>归一化位移（像素 Δ → 0~1000；缩放轴按渲染区尺寸）。</summary>
        private double DeltaToNormX(double dx)
        {
            double ox, oy, rw, rh;
            return TryGetFrameRect(out ox, out oy, out rw, out rh) ? dx * 1000.0 / rw : 0;
        }

        private double DeltaToNormY(double dy)
        {
            double ox, oy, rw, rh;
            return TryGetFrameRect(out ox, out oy, out rw, out rh) ? dy * 1000.0 / rh : 0;
        }

        private static int ClampNorm(double v) { return Math.Max(0, Math.Min(1000, (int)Math.Round(v))); }

        // ============ 重绘（全量清画重建；框量级 ≤ 十几个，性能无虞） ============

        /// <summary>画全部 ROI：非选中灰描边；选中主色描边 + 半透明底 + 四角手柄 + Id 标签。
        /// 手柄/框带 Tag 事件（MouseDown 区分移动/缩放），画布空白按下 = 画新框。</summary>
        private void RedrawRois()
        {
            var canvas = RoiStage.Overlay;
            canvas.Children.Clear();
            if (_vm == null || _vm.Rois.Count == 0) return;
            double ox, oy, rw, rh;
            if (!TryGetFrameRect(out ox, out oy, out rw, out rh)) return;

            var selBrush = (Brush)FindResource("ThemePrimary");
            var dimBrush = (Brush)FindResource("ThemeTextMuted");

            foreach (var roi in _vm.Rois)
            {
                bool sel = _vm.SelectedRoi != null && ReferenceEquals(roi, _vm.SelectedRoi);
                double x = ox + roi.X * rw / 1000.0;
                double y = oy + roi.Y * rh / 1000.0;
                double w = Math.Max(roi.W * rw / 1000.0, 6);
                double h = Math.Max(roi.H * rh / 1000.0, 6);

                var box = new Border
                {
                    BorderBrush = sel ? selBrush : dimBrush,
                    BorderThickness = new Thickness(sel ? 2.5 : 1.5),
                    CornerRadius = new CornerRadius(6),
                    Background = sel ? new SolidColorBrush(Color.FromArgb(0x22, 0xF9, 0x73, 0x16)) : Brushes.Transparent,
                    Cursor = Cursors.SizeAll,       // 框内按下 = 移动语义
                    Tag = roi,                      // MouseDown 处理器按 Tag 找回目标框
                };
                box.MouseLeftButtonDown += RoiBox_MouseDown;
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y);
                box.Width = w;
                box.Height = h;

                var label = new TextBlock
                {
                    Text = roi.Id,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = sel ? selBrush : dimBrush,
                    Margin = new Thickness(3, -18, 0, 0),
                    IsHitTestVisible = false,       // 标签不挡框的命中
                };
                box.Child = label;
                canvas.Children.Add(box);

                // 选中框四角手柄（8×8 主色方块；按下进入 Resize 态，对角锚定）
                if (sel)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        var grip = new Border
                        {
                            Width = 9, Height = 9,
                            Background = selBrush,
                            CornerRadius = new CornerRadius(2),
                            BorderBrush = (Brush)FindResource("ThemePanelBg"),
                            BorderThickness = new Thickness(1),
                            Cursor = c == 0 || c == 3 ? Cursors.SizeNWSE : Cursors.SizeNESW,  // 对角同向
                            Tag = c,
                        };
                        grip.MouseLeftButtonDown += Grip_MouseDown;
                        Canvas.SetLeft(grip, c == 0 || c == 2 ? x - 4.5 : x + w - 4.5);
                        Canvas.SetTop(grip, c == 0 || c == 1 ? y - 4.5 : y + h - 4.5);
                        Canvas.SetZIndex(grip, 10);     // 手柄压在框上（命中优先级自然高于框）
                        canvas.Children.Add(grip);
                    }
                }
            }
        }

        // ============ 鼠标交互（MouseDown 三分岔 / MouseMove 三态推进 / MouseUp 收口） ============

        /// <summary>框上按下：进 Move 态（记录起点 + 原坐标快照；捕获鼠标收后续事件）。</summary>
        private void RoiBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode != DragMode.None) return;
            var box = sender as Border;
            var roi = box != null ? box.Tag as RoiVm : null;
            if (roi == null || _vm == null) return;
            _vm.SelectedRoi = roi;              // 点框即选中（数值框/清单同步跟随）
            BeginDrag(DragMode.Move, roi, 0, e);
            e.Handled = true;
        }

        /// <summary>角手柄按下：进 Resize 态（_dragCorner 决定拖哪角）。</summary>
        private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode != DragMode.None) return;
            var grip = sender as Border;
            if (grip == null || _vm == null || _vm.SelectedRoi == null) return;
            BeginDrag(DragMode.Resize, _vm.SelectedRoi, (int)grip.Tag, e);
            e.Handled = true;
        }

        private void BeginDrag(DragMode mode, RoiVm roi, int corner, MouseButtonEventArgs e)
        {
            _dragMode = mode;
            _dragRoi = roi;
            _dragCorner = corner;
            _dragStart = e.GetPosition(RoiStage);
            _dragOrig[0] = roi.X; _dragOrig[1] = roi.Y; _dragOrig[2] = roi.W; _dragOrig[3] = roi.H;
            RoiStage.CaptureMouse();            // 拖出舞台也不丢事件
        }

        /// <summary>舞台按下（PreviewMouseDown 隧道，比框/手柄的冒泡按下先到）：
        /// 命中是框 Border（Tag=RoiVm）或手柄（Tag=corner int）→ 放行不处理，让各自
        /// 冒泡处理器接管；命中空白（其余一切）→ 取消选中 + 进 DrawNew 画新框。</summary>
        private void RoiStage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode != DragMode.None) return;
            if (_vm == null) return;

            var src = e.OriginalSource as FrameworkElement;
            if (src is Border && src.Tag is RoiVm) return;      // 框按下：RoiBox_MouseDown 接管（移动）
            if (src is Border && src.Tag is int) return;        // 手柄按下：Grip_MouseDown 接管（缩放）

            // —— 空白语义 ——
            _vm.SelectedRoi = null;             // 取消选中（清单高亮同步清除）
            _dragMode = DragMode.DrawNew;
            _dragStart = e.GetPosition(RoiStage);
            RoiStage.CaptureMouse();

            _rubber = new Border
            {
                BorderBrush = (Brush)FindResource("ThemePrimary"),
                BorderThickness = new Thickness(1.5, 1.5, 1.5, 1.5),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xF9, 0x73, 0x16)),
            };
            RoiStage.Overlay.Children.Add(_rubber);
            e.Handled = true;
        }

        /// <summary>拖拽推进（三态各一段）：Move 平移 / Resize 对角锚定 / DrawNew 橡皮筋。
        /// 全部实时回写（Move/Resize 直改 RoiVm 触发 INPC → RedrawRois；DrawNew 改橡皮筋几何）。</summary>
        private void RoiStage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode == DragMode.None) return;
            var p = e.GetPosition(RoiStage);
            double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
            double dnx = DeltaToNormX(dx), dny = DeltaToNormY(dy);

            if (_dragMode == DragMode.Move)
            {
                // 平移：原坐标 + 归一化位移（RoiVm setter 自带 0~1000 钳位）
                _dragRoi.X = ClampNorm(_dragOrig[0] + dnx);
                _dragRoi.Y = ClampNorm(_dragOrig[1] + dny);
            }
            else if (_dragMode == DragMode.Resize)
            {
                // 缩放：拖哪角哪角动，对角锚定（x1/y1 = 未拖动的对角归一化坐标）
                int x1 = _dragOrig[0], y1 = _dragOrig[1];
                int x2 = _dragOrig[0] + _dragOrig[2], y2 = _dragOrig[1] + _dragOrig[3];
                if (_dragCorner == 0) { x1 = ClampNorm(_dragOrig[0] + dnx); y1 = ClampNorm(_dragOrig[1] + dny); }
                else if (_dragCorner == 1) { x2 = ClampNorm(_dragOrig[0] + _dragOrig[2] + dnx); y1 = ClampNorm(_dragOrig[1] + dny); }
                else if (_dragCorner == 2) { x1 = ClampNorm(_dragOrig[0] + dnx); y2 = ClampNorm(_dragOrig[1] + _dragOrig[3] + dny); }
                else { x2 = ClampNorm(_dragOrig[0] + _dragOrig[2] + dnx); y2 = ClampNorm(_dragOrig[1] + _dragOrig[3] + dny); }
                // x1<x2 / y1<y2 校正（拖过对角时翻转）
                _dragRoi.X = Math.Min(x1, x2);
                _dragRoi.Y = Math.Min(y1, y2);
                _dragRoi.W = Math.Max(1, Math.Abs(x2 - x1));
                _dragRoi.H = Math.Max(1, Math.Abs(y2 - y1));
            }
            else // DrawNew：橡皮筋几何（像素直改，松手才落归一化值）
            {
                double ox, oy, rw, rh;
                if (!TryGetFrameRect(out ox, out oy, out rw, out rh)) return;
                double lx = Math.Min(_dragStart.X, p.X), ly = Math.Min(_dragStart.Y, p.Y);
                double lw = Math.Abs(p.X - _dragStart.X), lh = Math.Abs(p.Y - _dragStart.Y);
                Canvas.SetLeft(_rubber, Math.Max(ox, Math.Min(lx, ox + rw)));
                Canvas.SetTop(_rubber, Math.Max(oy, Math.Min(ly, oy + rh)));
                _rubber.Width = Math.Min(lw, rw);
                _rubber.Height = Math.Min(lh, rh);
            }
        }

        /// <summary>拖拽收口（PreviewMouseUp 隧道，任何元素收 Up 前先到——Move/Resize 的
        /// 按下源 Border 可能在 RedrawRois 全量重建中销毁，冒泡 Up 可能丢）：
        /// DrawNew 松手落框——≥阈值（归一化 15×15）才 AddRoiAt 新建并选中，过小视为误触放弃。</summary>
        private void RoiStage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode == DragMode.None) return;
            if (RoiStage.IsMouseCaptured) RoiStage.ReleaseMouseCapture();

            if (_dragMode == DragMode.DrawNew && _rubber != null)
            {
                int nx1, ny1, nx2, ny2;
                bool ok1 = TryPointToNorm(_dragStart, out nx1, out ny1);
                bool ok2 = TryPointToNorm(e.GetPosition(RoiStage), out nx2, out ny2);
                RoiStage.Overlay.Children.Remove(_rubber);
                _rubber = null;
                if (ok1 && ok2 && _vm != null)
                {
                    int w = Math.Abs(nx2 - nx1), h = Math.Abs(ny2 - ny1);
                    if (w >= 15 && h >= 15)         // 最小可辨识尺寸（防误触/防零面积）
                    {
                        _vm.AddRoiAt(Math.Min(nx1, nx2), Math.Min(ny1, ny2), w, h);
                    }
                }
            }
            _dragMode = DragMode.None;
            _dragRoi = null;
        }
    }
}
