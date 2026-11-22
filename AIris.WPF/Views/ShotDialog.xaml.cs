using System;
using System.IO;
using System.Windows;
using AIris.Core.Config;
using AIris.Core.Model;
using AIris.WPF.Services;

namespace AIris.WPF.Views
{
    /// <summary>存档详情弹窗 glue：注入 AuditRecord + 截图回显 + 导出 CSV（单条）。</summary>
    public partial class ShotDialog : Kaleidoscope.UI.Views.ChromeDialogWindow
    {
        private readonly AuditRecord _rec;

        public ShotDialog(AuditRecord rec)
        {
            InitializeComponent();
            _rec = rec;
            if (rec == null) return;
            Title = "存档详情 · " + rec.Sn;
            Badge.Text = rec.Sn + " · " + rec.Time.ToString("HH:mm:ss");
            bool ng = string.Equals(rec.Verdict, "NG", StringComparison.OrdinalIgnoreCase);
            VerdictText.Text = "判定：" + rec.Verdict + (ng ? " · " + rec.NgReason : "");
            VerdictText.Foreground = ng ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.SeaGreen;
            StepText.Text = "工步 " + rec.StepIndex + " · " + rec.StepName;
            RoiText.Text = "判定区域：" + rec.RoiId;
            RuleText.Text = "规则：" + (string.IsNullOrEmpty(rec.RuleSnapshot) ? "—" : rec.RuleSnapshot);
            EngineText.Text = "引擎：" + rec.EngineName;
            FileText.Text = "配方：" + rec.RecipeName + (string.IsNullOrEmpty(rec.ShotFile) ? "" : " · 存档：" + rec.ShotFile);

            // 截图回显
            if (!string.IsNullOrEmpty(rec.ShotFile))
            {
                var path = Path.Combine(AppPaths.AuditShotDir, rec.ShotFile);
                if (File.Exists(path))
                {
                    try
                    {
                        var img = new System.Windows.Media.Imaging.BitmapImage();
                        img.BeginInit();
                        img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        img.UriSource = new Uri(path);
                        img.EndInit();
                        img.Freeze();
                        ShotImage.Source = img;
                    }
                    catch { /* 回显失败不影响弹窗 */ }
                }
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var file = Path.Combine(AppPaths.AuditDir, "export_" + _rec.Time.ToString("yyyyMMdd_HHmmss") + ".csv");
            File.WriteAllText(file,
                "时间,SN,配方,工步,工步名,区域,判定,NG原因,规则快照,引擎,截图\r\n" +
                string.Join(",", new[] {
                    _rec.Time.ToString("yyyy-MM-dd HH:mm:ss"), _rec.Sn, _rec.RecipeName,
                    _rec.StepIndex.ToString(), _rec.StepName, _rec.RoiId, _rec.Verdict,
                    _rec.NgReason, _rec.RuleSnapshot, _rec.EngineName, _rec.ShotFile
                }),
                System.Text.Encoding.UTF8);
            ServiceHub.Host.Info("已导出：" + file, "导出完成");
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
