using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        /// <summary>
        /// 表单是否由 Magpie 模型快捷区回填（决定保存时是否标记 Gateway="magpie"）。
        /// 只有从 Magpie 列表点选过的卡才显示喜鹊品牌标 + 平台括号；
        /// 用户手填的普通 OpenAI 兼容卡保持直连语义，不会被误标成反代卡。
        /// </summary>
        private bool _fromMagpie;

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
                Gateway = _fromMagpie ? "magpie" : "",
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

        // ===== Magpie 反代快捷接入（2026-11-28）=====

        /// <summary>
        /// 拉取 Magpie 模型列表（async void：拉取期间界面不冻结，符合"UI 线程禁做网络 IO"红线）。
        /// 实际能力在 Kaleidoscope.Infer.InferClient.ListModelsAsync（通用模型目录发现，
        /// OpenAI 兼容 /v1/models；vLLM / llama.cpp / Ollama / Magpie 同一入口）。
        /// 失败只在提示行说明，不打断添加流程——用户可以继续手工填表
        /// （网关没开也不影响添加一张"以后用"的卡）。
        /// 异常全部就地转成提示文本：async void 里未捕获异常会直接崩进程。
        /// </summary>
        private async void MagpieFetch_Click(object sender, RoutedEventArgs e)
        {
            string url = (MagpieUrlBox.Text ?? "").Trim();
            if (url.Length == 0) url = EngineRegistry.MagpieDefaultBaseUrl;

            MagpieFetchBtn.IsEnabled = false;
            MagpieHint.Text = "正在拉取 " + url + " 的模型列表…";

            try
            {
                var ep = new Kaleidoscope.Infer.InferEndpoint
                {
                    Protocol = Kaleidoscope.Infer.InferProtocol.OpenAi,   // Magpie = OpenAI 兼容网关
                    BaseUrl = url,
                    TimeoutMs = 3000,
                };

                Kaleidoscope.Infer.InferModelListReply reply;
                var client = new Kaleidoscope.Infer.InferClient();
                try
                {
                    // await 而不是 GetResult()：本机 38ms，远程网关可能到 3s，同步等会冻结整个弹窗
                    reply = await client.ListModelsAsync(ep, System.Threading.CancellationToken.None)
                        .ConfigureAwait(true);   // 回 UI 线程再改控件（WPF 控件有线程亲和）
                }
                finally { client.Dispose(); }

                var models = reply.Models;
                MagpieModelCombo.ItemsSource = null;
                MagpieModelCombo.ItemsSource = models;
                MagpieModelCombo.IsEnabled = models.Count > 0;

                if (models.Count == 0)
                {
                    MagpieHint.Text = "拉取失败：" + (string.IsNullOrEmpty(reply.Error) ? "网关未返回模型" : reply.Error)
                        + "。确认 Magpie 已启动（magpie-windows-amd64.exe），或先手工填写下方表单。";
                    return;
                }

                int imageCount = models.Count(m => m.ImageSupportKnown && m.SupportsImage);
                int unknownCount = models.Count(m => !m.ImageSupportKnown);
                string detail = imageCount + " 个确认支持图像";
                if (unknownCount > 0) detail += "，" + unknownCount + " 个能力未知（服务未声明，需自行确认）";
                MagpieHint.Text = "已拉取 " + models.Count + " 个模型：" + detail + "。选中即自动填表。";

                // 默认选第一个（库已把视觉模型排前面），省一次点击
                MagpieModelCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MagpieHint.Text = "拉取失败：" + ex.Message;
            }
            finally
            {
                MagpieFetchBtn.IsEnabled = true;
            }
        }

        /// <summary>
        /// 选中 Magpie 模型 → 回填整张表单：
        /// 名称=展示名、协议=OpenAI 兼容（Magpie 走 /v1/chat/completions）、
        /// 地址=网关地址、模型名=完整 id（workbuddy/deepseek-v4.1-flash 这种，不能截断）。
        /// 纯文本模型仍允许选（列表可能有人想接别的用途），但提示检测会失败。
        /// </summary>
        private void MagpieModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var m = MagpieModelCombo.SelectedItem as Kaleidoscope.Infer.InferModelInfo;
            if (m == null) return;

            // 标题用友好名；Magpie 未给展示名时 DisplayName 等于 id，
            // 取最后一段（"workbuddy/deepseek-v4.1-flash" → "deepseek-v4.1-flash"），
            // 平台括号由 EngineCardDefinition.DisplayName 自动拼，避免标题重复平台名
            string title = m.DisplayName;
            if (string.IsNullOrEmpty(title) || string.Equals(title, m.Id, StringComparison.OrdinalIgnoreCase))
            {
                int slash = m.Id.LastIndexOf('/');
                title = slash >= 0 && slash < m.Id.Length - 1 ? m.Id.Substring(slash + 1) : m.Id;
            }
            _fromMagpie = true;   // 保存时标记 Gateway=magpie（卡片显示喜鹊品牌标 + 平台括号）
            NameBox.Text = title;
            LicenseBox.Text = "云端验证用 · 非离线部署";
            ProtocolCombo.SelectedIndex = 0;   // 第一项 = OpenAI 兼容（vLLM / llama-server / Magpie）
            UrlBox.Text = (MagpieUrlBox.Text ?? "").Trim();
            ModelBox.Text = m.Id;

            if (m.ImageSupportKnown && !m.SupportsImage)
            {
                MagpieHint.Text = "⚠ 该模型未声明图像输入，检测会出不了框；请选列表中标「图像」的模型。";
            }
            else if (!m.ImageSupportKnown)
            {
                MagpieHint.Text = "该模型未提供能力元数据，请自行确认支持图像输入（否则检测出不了框）。";
            }
        }
    }
}
