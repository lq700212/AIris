using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 引擎牌堆控制器（2026-11-28 用户定版：扑克牌收拢 ↔ 扇形展开）。
    ///
    /// 【要解决的问题】
    ///   引擎卡原来用 UniformGrid 四列等分，第 5 张起换行——换行后上下两排间距过近，
    ///   且卡片列表越长页面越难看。用户要求"要换行的时候就别换行了，做成扑克牌收拢"。
    ///
    /// 【形态】
    ///   · 收拢态 = 一叠牌：当前引擎在最上层（完整牌面），其余牌向下错开 2.5px 依次
    ///     露边形成厚度感；右下角胶囊提示"共 N 个引擎 · 点击展开"。
    ///   · 展开态 = 扇形：所有牌一行铺开（**永不换行**），中间高两边低 + 轻微旋转，
    ///     张数多到放不下时自动改为重叠排列（像手里捏着一把牌）；当前引擎主色描边。
    ///   · 点牌堆 = 展开；展开态点某张牌 = 切换引擎并收拢；点空白 = 收拢。
    ///
    /// 【为什么用 Canvas + RenderTransform 而不是 WrapPanel/UniformGrid】
    ///   Panel 重排会触发整棵可视树 Measure/Arrange，动画期间每帧重排 = 卡顿。
    ///   这里所有牌固定在 Canvas 的同一个基准位置（水平居中），只对
    ///   TranslateTransform / RotateTransform / ScaleTransform 做动画——
    ///   纯合成层变换，不触发重新布局，60fps 无压力。
    ///
    /// 【与悬停放大不打架】
    ///   布局动画只碰 Translate + Rotate，悬停只碰 Scale，两组属性互不覆盖。
    ///
    /// 【维护提示】
    ///   · 牌宽/间距/弧形深度都在 Relayout 顶部常量区，调观感只改那里；
    ///   · 新增/删除引擎卡后 ItemsControl 会重建容器，StatusChanged 会自动重排；
    ///   · 切换引擎后 Active 变化，调用方需调一次 Relayout(false) 刷新 Z 序。
    /// </summary>
    public class EngineDeckController
    {
        // —— 布局常量（调观感只改这里） ——

        /// <summary>牌面标准宽度（容器够宽时用这个）。</summary>
        private const double PreferredCardWidth = 264;

        /// <summary>牌面最小宽度（再窄内容就挤没了，改为重叠排列）。</summary>
        private const double MinCardWidth = 132;

        /// <summary>牌面高度（与 XAML ItemContainerStyle 的 Height 保持一致）。</summary>
        public const double CardHeight = 158;

        /// <summary>
        /// 牌面顶部留白：悬停会放大 1.06 + 上浮 6px，卡片顶部最多上探 ~11px。
        /// 不留这段缓冲，ScrollViewer 的 ScrollContentPresenter 会把卡片上边缘裁平
        /// （2026-11-28 用户实测：悬停时卡片顶部被"切掉"）。
        /// </summary>
        private const double TopPad = 14;

        /// <summary>悬停上浮距离（展开态：从牌堆里"拿起一张"的悬浮感）。</summary>
        private const double HoverLift = 6;

        /// <summary>收拢态悬停微浮距离：牌堆是一叠纸，浮太多会和纸边脱节；2px 只给"被点亮"的暗示。</summary>
        private const double HoverLiftCollapsed = 2;

        /// <summary>影子层模糊半径：越大影子越散、越"离地高"；16 配 5px 下移正好。</summary>
        private const double ShadowBlurRadius = 16;

        /// <summary>展开时牌与牌的水平间隙。</summary>
        private const double Gap = 14;

        /// <summary>扇形深度：最边上的牌比中间低多少像素（中间高两边低）。</summary>
        private const double ArcDepth = 14;

        /// <summary>扇形每档旋转角度（度）。</summary>
        private const double AnglePerStep = 1.8;

        /// <summary>收拢态每张垫底牌向下错开的像素（露边厚度）。</summary>
        private const double StackStep = 2.6;

        // —— 动画常量 ——

        private const double ExpandDurationMs = 360;
        private const double CollapseDurationMs = 280;
        private const double ExpandStaggerMs = 38;
        private const double CollapseStaggerMs = 16;
        private const double HoverDurationMs = 130;

        private readonly ItemsControl _deck;
        private readonly FrameworkElement _host;
        private readonly TextBlock _hint;
        private readonly Border _back1;
        private readonly Border _back2;
        private bool _expanded;

        /// <summary>当前是否展开（收拢 = false）。</summary>
        public bool IsExpanded { get { return _expanded; } }

        public EngineDeckController(ItemsControl deck, FrameworkElement host, TextBlock hint,
                                    Border back1, Border back2)
        {
            if (deck == null) throw new ArgumentNullException("deck");
            if (host == null) throw new ArgumentNullException("host");
            _deck = deck;
            _host = host;
            _hint = hint;
            _back1 = back1;
            _back2 = back2;

            // 容器首次生成 / 集合增删后重建 → 立即重排（无动画，避免页面加载时乱飞）
            _deck.ItemContainerGenerator.StatusChanged += OnGeneratorStatusChanged;
            // 窗口缩放 / 侧栏折叠 → 牌宽跟着变；拖动过程中不播动画
            _host.SizeChanged += OnHostSizeChanged;
            // 卡片集合本身变化（添加/删除引擎）→ 重新计算提示文案与布局
            if (_deck.Items is System.Collections.Specialized.INotifyCollectionChanged incc)
                incc.CollectionChanged += OnItemsCollectionChanged;
        }

        private void OnGeneratorStatusChanged(object sender, EventArgs e)
        {
            if (_deck.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                Relayout(false);
        }

        private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            Relayout(false);
        }

        private void OnItemsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // 容器可能尚未生成，交给 StatusChanged 兜底；这里只更新提示文案
            UpdateHint();
        }

        /// <summary>展开（幂等：已展开时只重排一次）。</summary>
        public void Expand()
        {
            if (_expanded) return;
            _expanded = true;
            Relayout(true);
        }

        /// <summary>收拢（幂等）。</summary>
        public void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            Relayout(true);
        }

        /// <summary>切换展开/收拢。</summary>
        public void Toggle()
        {
            if (_expanded) Collapse(); else Expand();
        }

        /// <summary>外部刷新（切换引擎 / 主题切换 / Mock 开关后调用）：只重排，不动展开状态。</summary>
        public void Refresh()
        {
            Relayout(false);
        }

        /// <summary>
        /// 核心布局：计算每张牌的目标位移/旋转并（可选）播放动画。
        /// 所有牌先固定到 Canvas 基准位（水平居中、顶部对齐），再用 Translate 推开。
        /// </summary>
        /// <param name="animate">true=播放展开/收拢动画；false=立即就位（首次加载/窗口缩放）</param>
        private void Relayout(bool animate)
        {
            int n = _deck.Items.Count;
            UpdateHint();
            if (n == 0) return;

            // 布局要变了，先清掉所有卡的悬停视觉残留（上浮位移 / 影子）
            ClearHover();

            double hostW = _host.ActualWidth;
            if (hostW < 40) return;   // 尚未完成测量，等 SizeChanged 再来

            // —— 1. 牌宽：优先 232；放不下就压缩；压到 132 以下改为重叠排列 ——
            double cardW = Math.Min(PreferredCardWidth, (hostW - Gap * (n - 1)) / n);
            if (cardW < MinCardWidth) cardW = MinCardWidth;

            // step = 相邻牌左边缘间距。不重叠时 = cardW + Gap；重叠时收缩到刚好放满容器
            double step = n > 1 ? Math.Min(cardW + Gap, (hostW - cardW) / (n - 1)) : 0;
            double totalW = cardW + step * (n - 1);
            double startX = Math.Max(0, (hostW - totalW) / 2);

            // Canvas 基准位：所有牌先放到水平居中（Translate 从这里起算）
            double baseLeft = Math.Max(0, (hostW - cardW) / 2);

            double center = (n - 1) / 2.0;
            int activeIndex = FindActiveIndex();
            // 收拢态只有最上面一张可见：优先当前引擎；Mock 模式（无 Active）显示第一张
            int topIndex = activeIndex >= 0 ? activeIndex : 0;

            // 牌堆纸边：宽度跟随牌宽；收拢态露出形成"一叠牌"厚度，展开态淡出
            if (_back1 != null)
            {
                _back1.Width = Math.Max(60, cardW - 6);
                AnimateOpacity(_back1, _expanded ? 0 : 0.95, animate, 0);
            }
            if (_back2 != null)
            {
                _back2.Width = Math.Max(50, cardW - 14);
                AnimateOpacity(_back2, _expanded ? 0 : 0.7, animate, 0);
            }

            for (int i = 0; i < n; i++)
            {
                var cp = _deck.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (cp == null) continue;

                // 宽度由控制器统一写入（XAML 里不绑，避免容器 DataContext 层级踩坑）
                cp.Width = cardW;
                cp.Height = CardHeight;
                Canvas.SetLeft(cp, baseLeft);
                Canvas.SetTop(cp, TopPad);   // 顶部留白 = 悬停放大/上浮的缓冲，防止被视口裁边

                var vm = cp.DataContext as EngineCardVm;
                bool isActive = vm != null && vm.Active;

                // Z 序：当前引擎永远压在最上面（收拢态"最外一层是当前模型"）
                Panel.SetZIndex(cp, isActive ? 1000 : i);

                // 收拢态非顶牌完全隐藏（否则多张牌的文字会叠成一团）；
                // 展开态全部淡入，按 index 依次浮现
                double targetOpacity = (_expanded || i == topIndex) ? 1.0 : 0.0;
                AnimateOpacity(cp, targetOpacity, animate, _expanded ? i * 20 : 0);
                cp.IsHitTestVisible = _expanded || i == topIndex;

                double targetX, targetY, targetAngle;
                if (_expanded)
                {
                    // 展开态：水平铺开 + 弧形（中间高两边低）+ 轻微扇形旋转
                    targetX = startX + i * step - baseLeft;
                    double t = center > 0 ? Math.Abs(i - center) / center : 0;   // 0=中间 1=最边
                    targetY = t * ArcDepth;
                    targetAngle = (i - center) * AnglePerStep;
                }
                else
                {
                    // 收拢态：全部回到基准位；非当前牌向下错开露边，形成一叠牌的厚度
                    targetX = 0;
                    targetY = (i == activeIndex || activeIndex < 0) ? 0 : (3 + i * StackStep);
                    targetAngle = 0;
                }

                ApplyTransform(cp, targetX, targetY, targetAngle, animate, i, n);
            }
        }

        /// <summary>找到当前引擎卡索引（Mock 模式下无 Active，返回 -1 = 全部不额外错开）。</summary>
        private int FindActiveIndex()
        {
            for (int i = 0; i < _deck.Items.Count; i++)
            {
                var vm = _deck.Items[i] as EngineCardVm;
                if (vm != null && vm.Active) return i;
            }
            return -1;
        }

        /// <summary>把一张牌的目标位移/旋转写进它的 TransformGroup（可选动画）。</summary>
        private void ApplyTransform(ContentPresenter cp, double x, double y, double angle,
                                    bool animate, int index, int count)
        {
            var tg = EnsureTransform(cp);
            if (tg == null) return;

            var rotate = tg.Children[1] as RotateTransform;
            var translate = tg.Children[2] as TranslateTransform;
            if (rotate == null || translate == null) return;

            if (!animate)
            {
                // 立即就位：先清掉可能残留的动画，再写静态值（否则动画会一直压着本地值）
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.X = x;
                translate.Y = y;
                rotate.Angle = angle;
                return;
            }

            // 依次飞出/飞回：展开从左到右（index 递增），收拢从右到左（倒序）
            double delay = _expanded
                ? index * ExpandStaggerMs
                : (count - 1 - index) * CollapseStaggerMs;
            double duration = _expanded ? ExpandDurationMs : CollapseDurationMs;
            var begin = TimeSpan.FromMilliseconds(delay);
            var dur = TimeSpan.FromMilliseconds(duration);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 注意：DoubleAnimation(to, duration) 的起点自动取当前值——打断上一次动画也平滑
            translate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(x, dur) { BeginTime = begin, EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(y, dur) { BeginTime = begin, EasingFunction = ease });
            rotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(angle, dur) { BeginTime = begin, EasingFunction = ease });
        }

        /// <summary>
        /// 确保元素上有**可动画**的 TransformGroup。
        /// 【为什么要代码创建】XAML Style Setter 里的 Freezable（TransformGroup 及其子变换）
        /// 会被 WPF 自动 Freeze 成跨元素共享的只读实例；对冻结对象调 BeginAnimation
        /// 会抛 InvalidOperationException。所以变换树一律在这里按容器现建，
        /// 通过本地值覆盖任何样式值——每个容器一份，互不影响。
        ///
        /// 结构约定：
        ///   [0]=Scale     （保留位；当前悬停不缩放，见 SetHover 注释）
        ///   [1]=Rotate    （扇形旋转）
        ///   [2]=Translate （布局位移：展开铺开 / 收拢错开）
        ///   [3]=Translate （悬停上浮，与布局位移分开——两组动画互不覆盖）
        /// </summary>
        private static TransformGroup EnsureTransform(FrameworkElement fe)
        {
            var tg = fe.RenderTransform as TransformGroup;
            if (tg != null && !tg.IsFrozen && tg.Children.Count >= 4)
                return tg;

            tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform());
            tg.Children.Add(new RotateTransform());
            tg.Children.Add(new TranslateTransform());
            tg.Children.Add(new TranslateTransform());
            fe.RenderTransform = tg;
            return tg;
        }

        /// <summary>当前悬停的影子层：淡出完成时靠它判断"是否已经移到别的牌"。</summary>
        private Border _hoveredShadow;

        /// <summary>
        /// 悬停：牌面上浮 + 独立影子层淡入 + 提到最上层（2026-11-28 用户定版"悬浮效果"）。
        ///
        /// 【模板结构契约】cardRoot.Children[0] = 影子层 CardShadow，Children[1] = 牌面 CardVisual。
        ///
        /// 【为什么不缩放】非整数倍 ScaleTransform 会让 WPF 走位图缩放、禁用 ClearType，
        /// 文字和圆角发虚；悬浮感只靠上浮 + 投影表达，牌面始终 1:1 渲染。
        ///
        /// 【为什么投影挂在独立影子层】Effect 挂在哪，哪个元素（含子树文字）就会被
        /// 先渲染成位图，ClearType 失效 → 悬停时文字发虚（"看着怪"的头号原因）。
        /// 影子层是纯黑圆角 Border、不含文字，模糊只作用在它身上，牌面文字始终清晰。
        ///
        /// 【为什么作用在牌面层而不是容器】容器尺寸 = 命中区，跟着上浮会让卡片下边缘
        /// 脱离鼠标 → MouseLeave → 回弹 → MouseEnter → 无限抖动。外层 Grid 固定不动。
        /// </summary>
        public void SetHover(Panel cardRoot, bool hover)
        {
            if (cardRoot == null || cardRoot.Children.Count < 2) return;
            var shadow = cardRoot.Children[0] as Border;
            var visual = cardRoot.Children[1] as Border;
            if (shadow == null || visual == null) return;

            var tg = EnsureTransform(visual);
            if (tg == null) return;
            var scale = tg.Children[0] as ScaleTransform;
            var lift = tg.Children[3] as TranslateTransform;
            if (scale == null || lift == null) return;

            // 缩放恒为 1（防御：旧版悬停动画可能残留，这里强制清掉）
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;

            var dur = TimeSpan.FromMilliseconds(HoverDurationMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            // 展开态才"拿起一张"（浮 6px）；收拢态是一叠纸，浮 2px 避免与纸边脱节
            double liftTarget = hover ? -(IsExpanded ? HoverLift : HoverLiftCollapsed) : 0;
            lift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(liftTarget, dur) { EasingFunction = ease });

            // 影子：淡入（懒挂模糊）/ 淡出（完成后摘模糊，省软件渲染开销）
            // 透明度只做 0↔1，浓淡由主题键 ThemeCardShadow 的 alpha 决定
            if (hover)
            {
                _hoveredShadow = shadow;
                if (shadow.Effect == null)
                    shadow.Effect = new BlurEffect { Radius = ShadowBlurRadius };
                shadow.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, dur) { EasingFunction = ease });
            }
            else
            {
                var target = shadow;
                var anim = new DoubleAnimation(0, dur) { EasingFunction = ease };
                anim.Completed += (s, e) =>
                {
                    if (!ReferenceEquals(_hoveredShadow, target)) target.Effect = null;
                };
                shadow.BeginAnimation(UIElement.OpacityProperty, anim);
            }

            // 悬停牌临时提到最上层（ZIndex 设在 ContentPresenter 上）
            var cp = FindContainer(cardRoot);
            if (cp != null)
            {
                var vm = cp.DataContext as EngineCardVm;
                Panel.SetZIndex(cp, hover ? 3000 : ((vm != null && vm.Active) ? 1000 : GetIndex(cp)));
            }
        }

        /// <summary>
        /// 清掉所有卡的悬停视觉残留（重排前调用）。
        /// 为什么需要：切换引擎后鼠标可能还停在刚点的牌上，那张牌会带着
        /// -6px 位移和影子被收拢进牌堆，下次展开时"凭空浮着"。
        /// </summary>
        private void ClearHover()
        {
            int n = _deck.Items.Count;
            for (int i = 0; i < n; i++)
            {
                var cp = _deck.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (cp == null) continue;
                var root = FindCardRoot(cp);
                if (root == null || root.Children.Count < 2) continue;

                var shadow = root.Children[0] as Border;
                if (shadow != null)
                {
                    shadow.BeginAnimation(UIElement.OpacityProperty, null);
                    shadow.Opacity = 0;
                    shadow.Effect = null;
                }

                var visual = root.Children[1] as Border;
                if (visual != null)
                {
                    var tg = visual.RenderTransform as TransformGroup;
                    if (tg != null && !tg.IsFrozen && tg.Children.Count >= 4)
                    {
                        var lift = tg.Children[3] as TranslateTransform;
                        if (lift != null)
                        {
                            lift.BeginAnimation(TranslateTransform.YProperty, null);
                            lift.Y = 0;
                        }
                    }
                }
            }
            _hoveredShadow = null;
        }

        /// <summary>ContentPresenter → 模板根 Grid（DataTemplate 的根元素）。</summary>
        private static Panel FindCardRoot(ContentPresenter cp)
        {
            int count = VisualTreeHelper.GetChildrenCount(cp);
            for (int i = 0; i < count; i++)
            {
                var panel = VisualTreeHelper.GetChild(cp, i) as Panel;
                if (panel != null) return panel;
            }
            return null;
        }

        /// <summary>从视觉层向上找 ItemContainer（ContentPresenter）。</summary>
        private static ContentPresenter FindContainer(DependencyObject d)
        {
            while (d != null && !(d is ContentPresenter))
                d = VisualTreeHelper.GetParent(d);
            return d as ContentPresenter;
        }

        private int GetIndex(ContentPresenter cp)
        {
            int i = _deck.ItemContainerGenerator.IndexFromContainer(cp);
            return i < 0 ? 0 : i;
        }

        /// <summary>透明度过渡（收拢态隐藏非顶牌 / 展开态依次浮现）。</summary>
        private static void AnimateOpacity(UIElement el, double target, bool animate, double delayMs)
        {
            if (el == null) return;
            if (!animate)
            {
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.Opacity = target;
                return;
            }
            el.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(target, TimeSpan.FromMilliseconds(200))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        /// <summary>刷新右下角提示胶囊文案。</summary>
        private void UpdateHint()
        {
            if (_hint == null) return;
            int n = _deck.Items.Count;
            if (_expanded)
                _hint.Text = "点击卡片切换 · 点空白收起";
            else
                _hint.Text = n > 0
                    ? ("共 " + n + " 个引擎 · 点击展开")
                    : "暂无引擎";
        }
    }
}
