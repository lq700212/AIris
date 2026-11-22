using System;
using System.IO;
using System.Windows;
using AIris.Core.Config;
using AIris.Core.Engine;
using AIris.Core.Model;
using AIris.WPF.Services;

namespace AIris.WPF.Views
{
    /// <summary>
    /// NG 详情弹窗 glue：注入 VerdictEventArgs 快照 + 判定瞬间的存档截图（若已落档）。
    /// "重检本步"回调 RunMonitor.Recheck 并关窗（NG 挂起态的人工闭环）。
    /// </summary>
    public partial class NgDetailDialog : Kaleidoscope.UI.Views.ChromeDialogWindow
    {
        public NgDetailDialog(VerdictEventArgs verdict, Recipe recipe)
        {
            InitializeComponent();
            if (verdict == null) return;
            Title = "NG 详情 · 工步 " + verdict.StepIndex;
            StepBadge.Text = "工步 " + verdict.StepIndex + (verdict.Step != null ? " · " + verdict.Step.Name : "");
            ReasonText.Text = verdict.Detail;
            RuleText.Text = "规则：" + (verdict.Step != null ? WorkstepEngine.RuleSnapshot(verdict.Step) : "—") +
                            " · 连续 " + verdict.StableNow + " 帧一致（需求 " + verdict.StableNeed + "）";
            BeaconText.Text = "声光：红灯闪烁 · 引导屏已切换警示画面（设备页可配置）";
            ShotText.Text = "判定瞬间已截图存档，到审计追溯页可复核判定依据";

            // 最近一条 NG 存档截图（有则回显）
            try
            {
                var rec = ServiceHub.AuditLatestNgShot();
                if (rec != null && !string.IsNullOrEmpty(rec.ShotFile))
                {
                    var path = Path.Combine(AppPaths.AuditShotDir, rec.ShotFile);
                    if (File.Exists(path))
                    {
                        var img = new System.Windows.Media.Imaging.BitmapImage();
                        img.BeginInit();
                        img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        img.UriSource = new Uri(path);
                        img.EndInit();
                        img.Freeze();
                        ShotImage.Source = img;
                        ShotText.Text = "存档：" + rec.ShotFile;
                    }
                }
            }
            catch { /* 截图回显失败不影响弹窗 */ }
        }

        private void Recheck_Click(object sender, RoutedEventArgs e)
        {
            ServiceHub.Monitor.Recheck();
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
