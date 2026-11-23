using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIris.Core.Config;
using AIris.Core.Model;
using Newtonsoft.Json;

namespace AIris.Core.Store
{
    /// <summary>
    /// 引擎卡存储：Data\EngineCards.json（现场数据，不入库；AGENTS.md 铁律 4）。
    ///
    /// 【文件结构】
    /// <code>
    /// {
    ///   "Overrides": [ { "Id": "qwen3-vl-http", "Protocol": "openai", "HttpBaseUrl": "http://127.0.0.1:8000",
    ///                    "HttpModel": "Qwen/Qwen3-VL-8B-Instruct", "ApiKey": "", "HttpTimeoutMs": 5000 } ],
    ///   "Custom":    [ { "Id": "custom-1a2b3c4d", "Name": "Qwen3-VL-4B", "LicenseNote": "…",
    ///                    "Protocol": "openai", "HttpBaseUrl": "…", "HttpModel": "…", … } ]
    /// }
    /// </code>
    ///
    /// 【为什么分两段】内置卡定义在 EngineRegistry 代码里（升级版本自动带新卡）；
    /// Overrides 只记"现场填过的连接参数"，按 id 覆盖到内置卡上——这样内置卡换名/换许可证说明
    /// 不用迁移旧文件，现场参数也不会丢。Custom 段是「＋ 添加引擎」创建的完整卡。
    ///
    /// 【并发】UI 单线程读写，但保持与 RecipeStore 一致的 lock 习惯（检测线程不碰本类）。
    /// 单文件损坏不拖垮启动：解析失败按空文件处理（后续保存会重建）。
    /// </summary>
    public class EngineCardStore
    {
        private readonly object _sync = new object();

        /// <summary>JSON 根结构（Newtonsoft 直接序列化，字段名与文件一致）。</summary>
        private class CardFile
        {
            public List<EngineCardDefinition> Overrides { get; set; } = new List<EngineCardDefinition>();
            public List<EngineCardDefinition> Custom { get; set; } = new List<EngineCardDefinition>();
        }

        private CardFile Read()
        {
            try
            {
                if (!File.Exists(AppPaths.EngineCardsFile)) return new CardFile();
                var f = JsonConvert.DeserializeObject<CardFile>(
                    File.ReadAllText(AppPaths.EngineCardsFile, System.Text.Encoding.UTF8));
                if (f == null) return new CardFile();
                if (f.Overrides == null) f.Overrides = new List<EngineCardDefinition>();
                if (f.Custom == null) f.Custom = new List<EngineCardDefinition>();
                return f;
            }
            catch
            {
                // 单文件损坏不拖垮启动：按空处理（用户下次保存即重建文件）
                return new CardFile();
            }
        }

        private void Write(CardFile f)
        {
            var dir = Path.GetDirectoryName(AppPaths.EngineCardsFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(AppPaths.EngineCardsFile,
                JsonConvert.SerializeObject(f, Formatting.Indented), System.Text.Encoding.UTF8);
        }

        /// <summary>取内置卡的参数覆盖（无记录返回 null，调用方用内置默认）。</summary>
        public EngineCardDefinition GetOverride(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_sync)
            {
                return Read().Overrides.FirstOrDefault(o =>
                    string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>保存某张卡的连接参数（内置卡=Overrides；自定义卡=Custom 原地更新）。</summary>
        public void SaveParams(EngineCardDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) return;
            lock (_sync)
            {
                var f = Read();
                if (def.IsBuiltin)
                {
                    var old = f.Overrides.FirstOrDefault(o =>
                        string.Equals(o.Id, def.Id, StringComparison.OrdinalIgnoreCase));
                    if (old != null) f.Overrides.Remove(old);
                    f.Overrides.Add(def.Clone());
                }
                else
                {
                    var old = f.Custom.FirstOrDefault(o =>
                        string.Equals(o.Id, def.Id, StringComparison.OrdinalIgnoreCase));
                    if (old != null) f.Custom.Remove(old);
                    f.Custom.Add(def.Clone());
                }
                Write(f);
            }
        }

        /// <summary>列出全部自定义卡（添加引擎追加的；按文件顺序）。</summary>
        public List<EngineCardDefinition> LoadCustom()
        {
            lock (_sync)
            {
                return Read().Custom
                    .Where(c => !string.IsNullOrEmpty(c.Id) && !string.IsNullOrEmpty(c.Name))
                    .Select(c => { c.IsBuiltin = false; c.IsMock = false; return c; })
                    .ToList();
            }
        }

        /// <summary>追加一张自定义卡（id 由调用方生成；重名/重复 id 返回 false）。</summary>
        public bool AddCustom(EngineCardDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id) || string.IsNullOrWhiteSpace(def.Name)) return false;
            lock (_sync)
            {
                var f = Read();
                if (f.Custom.Any(c => string.Equals(c.Id, def.Id, StringComparison.OrdinalIgnoreCase))) return false;
                if (f.Custom.Any(c => string.Equals(c.Name, def.Name, StringComparison.OrdinalIgnoreCase))) return false;
                def.IsBuiltin = false;
                def.IsMock = false;
                f.Custom.Add(def.Clone());
                Write(f);
                return true;
            }
        }

        /// <summary>删除一张自定义卡（内置卡不允许走到这里；返回是否删到）。</summary>
        public bool DeleteCustom(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_sync)
            {
                var f = Read();
                var hit = f.Custom.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
                if (hit > 0) Write(f);
                return hit > 0;
            }
        }

        /// <summary>当前文件是否已有该 id 的参数记录（首次启动迁移判断用）。</summary>
        public bool HasParams(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_sync)
            {
                var f = Read();
                return f.Overrides.Any(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase))
                    || f.Custom.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
