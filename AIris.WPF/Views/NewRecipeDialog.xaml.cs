using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AIris.Core.Detect;
using AIris.Core.Model;
using AIris.WPF.Services;

namespace AIris.WPF.Views
{
    /// <summary>向导草稿行。</summary>
    public class DraftRow
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Rule { get; set; }
    }

    /// <summary>
    /// 新建配方向导 glue：两步切换 + 草稿预填（按零件清单行生成"放置/出现 @ 待标画"工步）+ 创建落库。
    /// 创建成功 DialogResult=true，配方中心自动刷新。
    /// </summary>
    public partial class NewRecipeDialog : Kaleidoscope.UI.Views.ChromeDialogWindow
    {
        private Recipe _draft;
        private int _step = 1;

        public NewRecipeDialog()
        {
            InitializeComponent();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_step == 1)
            {
                var name = (NameBox.Text ?? "").Trim();
                var parts = (PartsBox.Text ?? "").Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                if (name.Length == 0 || parts.Count == 0)
                {
                    ServiceHub.Host.Info("请先填写产品名称和零件清单（顿号分隔）。", "信息不全");
                    return;
                }
                BuildDraft(name, parts);
                ShowStep(2);
                return;
            }
            // 第 2 步确认创建：落库 + 失败返回
            try
            {
                ServiceHub.Recipes.Save(_draft);
                DialogResult = true;    // ShowDialog() 返回值只认它（旧设自定义属性恒 false——同 ConfirmDialog 修复）
                Close();
            }
            catch (Exception ex)
            {
                ServiceHub.Host.Info("保存失败：" + ex.Message, "创建失败");
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e) { ShowStep(1); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { Close(); }

        /// <summary>组草稿：零件行 → 工步（放置/出现 @ 待标画）+ 末尾整检步。</summary>
        private void BuildDraft(string name, List<string> parts)
        {
            _draft = new Recipe { Name = name, Description = "", Parts = parts, Enabled = false };
            int idx = 1;
            foreach (var p in parts)
            {
                string cat = MockDetectionEngine.ExtractKeyword(p);
                string qty = System.Text.RegularExpressions.Regex.Match(p, @"(\d+)\s*(颗|个|只|片|条|块)").Success
                    ? "数量=" + System.Text.RegularExpressions.Regex.Match(p, @"(\d+)").Groups[1].Value
                    : "出现";
                _draft.Steps.Add(new RecipeStep
                {
                    Name = (idx == 1 ? "放置 " : "安装 ") + p,
                    ExpectedPart = p,
                    Expectation = StepExpectation.Appear,
                    RoiId = "",
                    Rule = new StepRule()
                });
                idx++;
            }
            _draft.Steps.Add(new RecipeStep
            {
                Name = "整检 · 零件齐套",
                ExpectedPart = string.Join("、", parts),
                Expectation = StepExpectation.FinalCheck,
                RoiId = "",
                Rule = new StepRule()
            });

            DraftList.ItemsSource = _draft.Steps.Select((s, i) => new DraftRow
            {
                Index = i + 1,
                Name = s.Name,
                Rule = (i == _draft.Steps.Count - 1) ? "齐套 @ 全画面" : "出现 @ 待标画"
            }).ToList();
        }

        private void ShowStep(int n)
        {
            _step = n;
            Step1Panel.Visibility = n == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = n == 2 ? Visibility.Visible : Visibility.Collapsed;
            PrevBtn.Visibility = n == 2 ? Visibility.Visible : Visibility.Collapsed;
            NextBtn.Content = n == 1 ? "下一步" : "创建配方";
            UpdateStepIndicator(n);
        }

        /// <summary>
        /// 步骤指示器视觉切换（设计稿 .steps 口径）：当前步 = 橙底白字 + 文字加粗，
        /// 未到步 = 面板底 + 灰字。颜色走主题键，深浅色自动跟随。
        /// </summary>
        private void UpdateStepIndicator(int n)
        {
            var prim = (System.Windows.Media.Brush)FindResource("ThemePrimaryGrad");
            var primBorder = (System.Windows.Media.Brush)FindResource("ThemePrimary");
            var panel2 = (System.Windows.Media.Brush)FindResource("ThemePanel2Bg");
            var border = (System.Windows.Media.Brush)FindResource("ThemeBorder");
            var fg = (System.Windows.Media.Brush)FindResource("ThemeTextFg");
            var muted = (System.Windows.Media.Brush)FindResource("ThemeTextMuted");
            var onPri = (System.Windows.Media.Brush)FindResource("ThemeOnPrimary");

            bool first = n == 1;
            StepDot1.Background = first ? prim : panel2;
            StepDot1.BorderBrush = first ? primBorder : border;
            StepNum1.Foreground = first ? onPri : muted;
            StepLbl1.Foreground = first ? fg : muted;
            StepLbl1.FontWeight = first ? FontWeights.SemiBold : FontWeights.Normal;

            StepDot2.Background = first ? panel2 : prim;
            StepDot2.BorderBrush = first ? border : primBorder;
            StepNum2.Foreground = first ? muted : onPri;
            StepLbl2.Foreground = first ? muted : fg;
            StepLbl2.FontWeight = first ? FontWeights.Normal : FontWeights.SemiBold;
        }
    }
}
