using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIris.WPF.ViewModels;
using Kaleidoscope.UI.Views;

namespace AIris.WPF.Views
{
    /// <summary>
    /// ROI 标画交互控制器（V0.1.11 抽出）：页面内画布与全屏弹窗共用同一套
    /// 「拖框移动 / 拖角缩放 / 空白拖画新框 / 数值双向联动」交互，杜绝两份实现漂移。
    ///
    /// 【为什么要抽出来】
    ///   旧版交互写死在 RecipeEditorPage.xaml.cs，全屏弹窗需要第二个 KxImageStage 时
    ///   只能复制一份——两边任何一处修 bug 就会漏另一边（ROI 数值↔画布联动曾经就漏过）。
    ///   控制器只依赖 VM + 舞台 + 外层视口三个对象，宿主页面/弹窗各 new 一个即可。
    ///
    /// 【画布比例口径（本版核心修正）】
    ///   舞台不再铺满整个卡片（旧版固定 Height=240 的超宽条，ROI 框映射整条，
    ///   与监控台 4:3 画面完全对不上——CHANGELOG V0.1.10 遗留待办）。
    ///   新口径：舞台尺寸 = 外层视口内「按画面比例内接」的矩形（与监控台画面同比例），
    ///   比例源 = FrameImage 实际像素宽高（真机换分辨率零改动），无帧时回退 Mock 帧 4:3。
    ///   视口放不下就等比缩小、富余就等比放大，舞台始终居中。
    ///   坐标换算仍是归一化 0~1000 ↔ 画面渲染区像素（FrameRect 同源），因此：
    ///   ROI 框在编辑器、全屏弹窗、监控台三处落在画面同一位置。
    ///
    /// 【交互三件（命中优先级：手柄 &gt; 已有框 &gt; 空白）】
    ///   ①拖已有框 = 移动（起点算好归一化坐标，Δpx→Δ归一化实时回写 VM）
    ///   ②四角手柄 = 缩放（拖哪角哪角动，对角锚定）
    ///   ③空白按下拖拽 = 画新框（橡皮筋预览，松手≥阈值才落 ROI，过小放弃防误触）
    ///
    /// 【生命周期】构造即挂事件（舞台/视口），Bind(vm) 换数据源（可重复），
    ///   Dispose 摘干净（全屏弹窗 Closed 必须调，否则 VM 一直引用已关闭的视觉树）。
    ///   UI 线程使用（WPF 常规纪律）。
    /// </summary>
    public sealed class RoiStageController : IDisposable
    {
        /// <summary>无帧时的默认画面比例（Mock 相机 1280×960 = 4:3，与主界面空态口径一致）。</summary>
        private const double DefaultAspect = 1280.0 / 960.0;

        /// <summary>舞台最小边长（容器被压到极小时的兜底，避免 0 尺寸导致除零/不可命中）。</summary>
        private const double MinStageSide = 40.0;

        /// <summary>拖拽状态机（三态互斥；None = 空闲）。</summary>
        private enum DragMode { None, Move, Resize, DrawNew }

        private readonly KxImageStage _stage;        // 取像舞台（画面 + 内置 Overlay 画布）
        private readonly FrameworkElement _viewport;  // 舞台容器（尺寸约束源：Border/Grid）

        private RecipeEditorViewModel _vm;
        private RoiVm _lastHooked;                   // 已挂属性监听的选中 ROI（避免重复订阅）

        private DragMode _dragMode = DragMode.None;
        private RoiVm _dragRoi;                      // Move/Resize 的目标框
        private int _dragCorner;                     // Resize 手柄角标（0=左上 1=右上 2=左下 3=右下）
        private Point _dragStart;                    // 按下点（舞台逻辑像素坐标）
        private readonly int[] _dragOrig = new int[4];   // 按下时的 X/Y/W/H 快照（Move/Resize 基准）
        private Border _rubber;                      // DrawNew 橡皮筋预览框

        /// <summary>
        /// 绑定一个舞台与它的外层视口。
        /// stage：KxImageStage（内置 Overlay 供画框）；viewport：尺寸约束容器（舞台按画面比例内接其中）。
        /// 构造只挂事件，真正画框要等 <see cref="Bind"/> 注入 VM。
        /// </summary>
        public RoiStageController(KxImageStage stage, FrameworkElement viewport)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            if (viewport == null) throw new ArgumentNullException(nameof(viewport));
            _stage = stage;
            _viewport = viewport;

            // 命中垫兜底：KxImageStage 是无 Background 的 Grid，空白处命中会穿透到外层
            // Border（事件根本不到 Stage）——显式给透明背景保证整面可命中（空白画新框的前提）
            _stage.Background = Brushes.Transparent;
            // 空白按下走 PreviewMouseDown（隧道事件）：比冒泡 MouseLeftButtonDown 更早到达，
            // 不会被框/手柄的 e.Handled 拦掉——处理器内部按"命中是否 ROI 元素"自行分岔
            _stage.PreviewMouseDown += Stage_MouseDown;
            _stage.PreviewMouseUp += Stage_MouseUp;
            _stage.MouseMove += Stage_MouseMove;
            _stage.SizeChanged += Stage_SizeChanged;
            _viewport.SizeChanged += Viewport_SizeChanged;
        }

        // ============ 绑定/解绑（VM 是唯一数据源） ============

        /// <summary>绑定 VM（页面 DataContext 变化 / 弹窗构造时调用；重复绑定同一实例只重绘）。
        /// 订阅三路联动：VM.SelectedRoi 切换、ROI 集合增删、选中 ROI 的 X/Y/W/H 数值变化。</summary>
        public void Bind(RecipeEditorViewModel vm)
        {
            if (ReferenceEquals(_vm, vm))
            {
                UpdateStageSize();
                RedrawRois();
                return;
            }
            Unbind();
            _vm = vm;
            if (_vm != null)
            {
                _vm.PropertyChanged += Vm_PropertyChanged;
                _vm.Rois.CollectionChanged += Rois_CollectionChanged;
                HookRoi(_vm.SelectedRoi);
            }
            UpdateStageSize();   // 先按当前帧比例定舞台尺寸，再画框
            RedrawRois();
        }

        private void Unbind()
        {
            if (_vm == null) return;
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.Rois.CollectionChanged -= Rois_CollectionChanged;
            DetachRoiHook(_lastHooked);
            _vm = null;
        }

        /// <summary>摘干净：解绑 VM、摘舞台/视口事件、终止进行中的拖拽、清空叠加层。
        /// 全屏弹窗关闭必须调用；宿主页面常驻可不调（进程退出自然回收）。</summary>
        public void Dispose()
        {
            Unbind();
            _stage.PreviewMouseDown -= Stage_MouseDown;
            _stage.PreviewMouseUp -= Stage_MouseUp;
            _stage.MouseMove -= Stage_MouseMove;
            _stage.SizeChanged -= Stage_SizeChanged;
            _viewport.SizeChanged -= Viewport_SizeChanged;

            if (_stage.IsMouseCaptured) _stage.ReleaseMouseCapture();
            _dragMode = DragMode.None;
            _dragRoi = null;
            _rubber = null;
            _stage.Overlay.Children.Clear();
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
            else if (e.PropertyName == "FrameImage")
            {
                // 帧尺寸变化 = 画面比例变化（启停取像/真机切分辨率）：
                // 先按新比例重算舞台，再重绘——旧版没订阅这里，帧到达后框仍在整幅舞台坐标系
                UpdateStageSize();
                RedrawRois();
            }
        }

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

        // ============ 舞台比例（与主界面画面一致） ============

        /// <summary>当前画面宽高比：有帧按帧实际像素（真机换分辨率自动跟随），无帧回退 Mock 4:3。</summary>
        private double CurrentAspect()
        {
            var frame = _vm != null ? _vm.FrameImage : null;
            if (frame != null && frame.PixelWidth > 0 && frame.PixelHeight > 0)
                return (double)frame.PixelWidth / frame.PixelHeight;
            return DefaultAspect;
        }

        /// <summary>按视口尺寸与画面比例求舞台内接矩形（居中，等比缩放）：
        /// 宽取满视口 → 高按比例算，超出视口高则反过来由高推宽；
        /// 结果写舞台 Width/Height，舞台在 XAML 里 Horizontal/VerticalAlignment=Center。
        /// 视口/舞台未布局（尺寸 &lt; 1）时跳过，等 SizeChanged 再来。</summary>
        private void UpdateStageSize()
        {
            double vw = _viewport.ActualWidth, vh = _viewport.ActualHeight;
            if (vw < 1 || vh < 1) return;

            double aspect = CurrentAspect();
            double w = vw, h = w / aspect;
            if (h > vh) { h = vh; w = h * aspect; }
            w = Math.Max(MinStageSide, Math.Floor(w));
            h = Math.Max(MinStageSide, Math.Floor(h));

            // 尺寸没变不重设（避免无谓的布局/重绘；阈值 0.5 吸收取整抖动）
            if (Math.Abs(_stage.Width - w) < 0.5 && Math.Abs(_stage.Height - h) < 0.5) return;
            _stage.Width = w;
            _stage.Height = h;
        }

        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateStageSize(); }

        private void Stage_SizeChanged(object sender, SizeChangedEventArgs e) { RedrawRois(); }

        // ============ 坐标换算（归一化 0~1000 ↔ 舞台像素） ============

        /// <summary>画面渲染区：帧非空按帧实际像素宽高求舞台内接矩形（Uniform 口径）；
        /// 帧空回退整舞台（无底图也允许画框，坐标系即舞台本身）。换算抽 FrameRect 共用。
        /// 兜底：舞台已按帧比例定尺寸，故正常情况渲染区≈整舞台；FrameRect 仍保留，
        /// 用于浮点取整/真机非整数比例时的精确内接。</summary>
        private bool TryGetFrameRect(out double ox, out double oy, out double rw, out double rh)
        {
            ox = oy = rw = rh = 0;
            double gw = _stage.ActualWidth, gh = _stage.ActualHeight;
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

        /// <summary>主题画刷查找（舞台 → Application 两级；舞台未入视觉树时 App 资源兜底）。
        /// 禁止写死色值（AGENTS 铁律）：全部走 Theme* 键，主题热切换即时生效。</summary>
        private Brush BrushOf(string key)
        {
            Brush b = _stage.TryFindResource(key) as Brush;
            if (b == null && Application.Current != null) b = Application.Current.TryFindResource(key) as Brush;
            return b ?? Brushes.Orange;
        }

        // ============ 重绘（全量清画重建；框量级 ≤ 十几个，性能无虞） ============

        /// <summary>画全部 ROI：非选中灰描边；选中主色描边 + 半透明底 + 四角手柄 + Id 标签。
        /// 手柄/框带 Tag 事件（MouseDown 区分移动/缩放），画布空白按下 = 画新框。</summary>
        private void RedrawRois()
        {
            var canvas = _stage.Overlay;
            canvas.Children.Clear();
            if (_vm == null || _vm.Rois.Count == 0) return;
            double ox, oy, rw, rh;
            if (!TryGetFrameRect(out ox, out oy, out rw, out rh)) return;

            var selBrush = BrushOf("ThemePrimary");
            var dimBrush = BrushOf("ThemeTextMuted");
            var fillBrush = BrushOf("ThemePrimaryFill");

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
                    Background = sel ? fillBrush : Brushes.Transparent,
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
                    Text = string.IsNullOrEmpty(roi.Name) ? roi.Id : roi.Display,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = sel ? selBrush : dimBrush,
                    Margin = new Thickness(3, -18, 0, 0),
                    IsHitTestVisible = false,       // 标签不挡框的命中
                };
                box.Child = label;
                canvas.Children.Add(box);

                // 选中框四角手柄（9×9 主色方块；按下进入 Resize 态，对角锚定）
                if (sel)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        var grip = new Border
                        {
                            Width = 9, Height = 9,
                            Background = selBrush,
                            CornerRadius = new CornerRadius(2),
                            BorderBrush = BrushOf("ThemePanelBg"),
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
            _dragStart = e.GetPosition(_stage);
            _dragOrig[0] = roi.X; _dragOrig[1] = roi.Y; _dragOrig[2] = roi.W; _dragOrig[3] = roi.H;
            _stage.CaptureMouse();              // 拖出舞台也不丢事件
        }

        /// <summary>舞台按下（PreviewMouseDown 隧道，比框/手柄的冒泡按下先到）：
        /// 命中是框 Border（Tag=RoiVm）或手柄（Tag=corner int）→ 放行不处理，让各自
        /// 冒泡处理器接管；命中空白（其余一切）→ 取消选中 + 进 DrawNew 画新框。</summary>
        private void Stage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode != DragMode.None) return;
            if (_vm == null) return;

            var src = e.OriginalSource as FrameworkElement;
            if (src is Border && src.Tag is RoiVm) return;      // 框按下：RoiBox_MouseDown 接管（移动）
            if (src is Border && src.Tag is int) return;        // 手柄按下：Grip_MouseDown 接管（缩放）

            // —— 空白语义 ——
            _vm.SelectedRoi = null;             // 取消选中（清单高亮同步清除）
            _dragMode = DragMode.DrawNew;
            _dragStart = e.GetPosition(_stage);
            _stage.CaptureMouse();

            _rubber = new Border
            {
                BorderBrush = BrushOf("ThemePrimary"),
                BorderThickness = new Thickness(1.5, 1.5, 1.5, 1.5),
                CornerRadius = new CornerRadius(6),
                Background = BrushOf("ThemePrimaryFill"),
            };
            _stage.Overlay.Children.Add(_rubber);
            e.Handled = true;
        }

        /// <summary>拖拽推进（三态各一段）：Move 平移 / Resize 对角锚定 / DrawNew 橡皮筋。
        /// 全部实时回写（Move/Resize 直改 RoiVm 触发 INPC → RedrawRois；DrawNew 改橡皮筋几何）。</summary>
        private void Stage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode == DragMode.None) return;
            var p = e.GetPosition(_stage);
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
                if (_rubber == null) return;
                double ox, oy, rw, rh;
                if (!TryGetFrameRect(out ox, out oy, out rw, out rh)) return;
                double lx = Math.Min(_dragStart.X, p.X), ly = Math.Min(_dragStart.Y, p.Y);
                double lw = Math.Abs(p.X - _dragStart.X), lh = Math.Abs(p.Y - _dragStart.Y);

                // 橡皮筋裁剪到画面渲染区（letterbox 空白处拖拽不越界）
                double cx = Math.Max(ox, Math.Min(lx, ox + rw));
                double cy = Math.Max(oy, Math.Min(ly, oy + rh));
                Canvas.SetLeft(_rubber, cx);
                Canvas.SetTop(_rubber, cy);
                _rubber.Width = Math.Max(1, Math.Min(lx + lw, ox + rw) - cx);
                _rubber.Height = Math.Max(1, Math.Min(ly + lh, oy + rh) - cy);
            }
        }

        /// <summary>拖拽收口（PreviewMouseUp 隧道，任何元素收 Up 前先到——Move/Resize 的
        /// 按下源 Border 可能在 RedrawRois 全量重建中销毁，冒泡 Up 可能丢）：
        /// DrawNew 松手落框——≥阈值（归一化 15×15）才 AddRoiAt 新建并选中，过小视为误触放弃。</summary>
        private void Stage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode == DragMode.None) return;
            if (_stage.IsMouseCaptured) _stage.ReleaseMouseCapture();

            if (_dragMode == DragMode.DrawNew && _rubber != null)
            {
                int nx1, ny1, nx2, ny2;
                bool ok1 = TryPointToNorm(_dragStart, out nx1, out ny1);
                bool ok2 = TryPointToNorm(e.GetPosition(_stage), out nx2, out ny2);
                _stage.Overlay.Children.Remove(_rubber);
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
