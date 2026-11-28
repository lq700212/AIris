using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 检测引擎页 glue：只做交互转发，布局与动画全部在 EngineDeckController。
    ///
    /// 交互口径（2026-11-28 用户定版）：
    ///   · 收拢态点牌堆（任意位置）= 扇形展开；
    ///   · 展开态点非当前牌 = 切换引擎 + 收拢；
    ///   · 展开态点当前牌 / 点空白 = 收拢；
    ///   · 展开态点当前牌 / 点空白 = 收拢；
    ///   · 悬停 = 上浮 + 投影（不缩放：非整数倍缩放会让文字发虚）。    /// </summary>
    public partial class EnginePage : UserControl
    {
        private EngineDeckController _deck;

        public EnginePage()
        {
            InitializeComponent();
            // 控制器需要容器生成完毕 + 有实际宽度才能算牌宽，放在 Loaded 里建
            Loaded += OnPageLoaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (_deck == null)
                _deck = new EngineDeckController(Deck, DeckHost, DeckHintText, DeckBack1, DeckBack2);
            _deck.Refresh();
        }

        /// <summary>点牌堆空白：收拢 ↔ 展开（卡片点击会在 Card_MouseLeftButtonDown 里 Handled）。</summary>
        private void DeckHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_deck == null) return;
            _deck.Toggle();
            e.Handled = true;
        }

        /// <summary>点某张牌：收拢态先展开；展开态非当前牌 → 切换并收起；当前牌 → 收起。</summary>
        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_deck == null) return;
            var fe = sender as FrameworkElement;
            var vm = fe != null ? fe.DataContext as EngineCardVm : null;
            if (vm == null) return;

            if (!_deck.IsExpanded)
            {
                // 收拢态只露出最上面一张，点它先展开看全部牌
                _deck.Expand();
            }
            else if (!vm.Active)
            {
                // 直接切换（2026-11-28 用户定版：不再弹确认框），切完自动收拢牌堆
                var owner = DataContext as EngineViewModel;
                if (owner != null)
                {
                    owner.SwitchTo(vm);
                    _deck.Refresh();
                    _deck.Collapse();
                }
            }
            else
            {
                _deck.Collapse();
            }

            // 阻止冒泡到 DeckHost 再 Toggle 一次（否则点了牌又弹开）
            e.Handled = true;
        }

        private void Card_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_deck == null) return;
            var root = sender as Panel;
            if (root != null) _deck.SetHover(root, true);
        }

        private void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_deck == null) return;
            var root = sender as Panel;
            if (root != null) _deck.SetHover(root, false);
        }
    }
}
