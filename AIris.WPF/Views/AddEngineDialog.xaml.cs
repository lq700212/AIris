using System;
using System.Linq;
using System.Windows;
using AIris.Core.Detect;
using AIris.Core.Model;
using AIris.WPF.Services;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 添加检测引擎弹窗 glue（设计稿 m-add-engine）：
    /// 校验表单 → 生成 custom-xxxxxxxx 注册 id → EngineCardStore.AddCustom 落盘 → DialogResult=true。
    /// 引擎页收到 true 后重建卡列；用户先「测试连接」再「切换到此引擎」（与内置卡同一操作路径）。
    /// 内置卡名称不允许重复（避免卡片列表出现两张同名卡，现场分不清）。
    /// </summary>
    public partial class AddEngineDialog : Kaleidoscope.UI.Views.ChromeDialogWindow
    {
        public AddEngineDialog()
        {
            InitializeComponent();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            string name = (NameBox.Text ?? "").Trim();
            string license = (LicenseBox.Text ?? "").Trim();
            string url = (UrlBox.Text ?? "").Trim();
            string model = (ModelBox.Text ?? "").Trim();

            if (name.Length == 0)
            {
                ServiceHub.Host.Info("请先填写引擎名称（卡片标题）。", "信息不全");
                return;
            }
            // 重名检查（内置 + 自定义）：卡片列表按名称辨识，重名现场无法区分
            bool dup = EngineRegistry.AllCards(ServiceHub.EngineCards)
                .Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (dup)
            {
                ServiceHub.Host.Info("已有同名引擎卡「" + name + "」，请换一个名称。", "名称重复");
                return;
            }
            if (url.Length == 0)
            {
                ServiceHub.Host.Info("请填写 HTTP 推理服务地址（如 http://127.0.0.1:8000）。", "信息不全");
                return;
            }
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ServiceHub.Host.Info("服务地址格式不对，应为 http:// 或 https:// 开头。", "地址无效");
                return;
            }

            var def = new EngineCardDefinition
            {
                // 注册 id 不进 UI：custom- + 8 位随机串，够用且不与内置 id 冲突
                Id = "custom-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = name,
                LicenseNote = license.Length == 0 ? "许可证：未标注 · 请自行确认" : license,
                IsBuiltin = false,
                IsMock = false,
                Protocol = (ProtocolCombo.SelectedValue as string) ?? "openai",
                HttpBaseUrl = url,
                HttpModel = model,
                ApiKey = (KeyBox.Text ?? "").Trim(),
                HttpTimeoutMs = 5000,
            };

            if (!ServiceHub.EngineCards.AddCustom(def))
            {
                ServiceHub.Host.Info("添加失败：名称已存在或文件写入失败。", "添加失败");
                return;
            }

            DialogResult = true;   // ShowDialog() 返回值只认它（与 ConfirmDialog/NewRecipeDialog 同口径）
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
