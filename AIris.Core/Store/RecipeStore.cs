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
    /// 配方存储：Data\Recipes\&lt;Name&gt;.json 每配方一文件（Newtonsoft 序列化 Recipe 全字段）。
    /// 启用语义：Enabled 标记全局唯一——Enable 时自动把其它配方置停用（设计稿配方卡"启用中"唯一）。
    /// 线程安全：内部 lock（配方页/监控台/运行线程可能同时读写）。
    /// </summary>
    public class RecipeStore
    {
        private readonly object _sync = new object();

        /// <summary>列出全部配方（按更新时间倒序；目录不存在返回空）。</summary>
        public List<Recipe> ListAll()
        {
            lock (_sync)
            {
                var list = new List<Recipe>();
                if (!Directory.Exists(AppPaths.RecipeDir)) return list;
                foreach (var f in Directory.GetFiles(AppPaths.RecipeDir, "*.json"))
                {
                    try
                    {
                        var r = JsonConvert.DeserializeObject<Recipe>(File.ReadAllText(f, System.Text.Encoding.UTF8));
                        if (r != null) list.Add(r);
                    }
                    catch { /* 单文件损坏不拖垮列表 */ }
                }
                return list.OrderByDescending(r => r.UpdatedAt).ToList();
            }
        }

        /// <summary>取启用中的配方（无则 null）。</summary>
        public Recipe GetEnabled()
        {
            return ListAll().FirstOrDefault(r => r.Enabled);
        }

        /// <summary>按名读取。</summary>
        public Recipe Get(string name)
        {
            return ListAll().FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>保存（新建/编辑统一入口；文件名 = 配方名）。</summary>
        public void Save(Recipe recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.Name)) throw new ArgumentException("配方名不能为空");
            recipe.UpdatedAt = DateTime.Now;
            lock (_sync)
            {
                if (!Directory.Exists(AppPaths.RecipeDir)) Directory.CreateDirectory(AppPaths.RecipeDir);
                var path = Path.Combine(AppPaths.RecipeDir, SafeFileName(recipe.Name) + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(recipe, Formatting.Indented), System.Text.Encoding.UTF8);
            }
        }

        /// <summary>删除配方（按名；删除后返回是否删到了文件）。</summary>
        public bool Delete(string name)
        {
            lock (_sync)
            {
                var path = Path.Combine(AppPaths.RecipeDir, SafeFileName(name) + ".json");
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
        }

        /// <summary>启用一份配方（全局唯一：其它全部置停用）。</summary>
        public void Enable(string name)
        {
            foreach (var r in ListAll())
            {
                bool target = string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase);
                if (r.Enabled != target) { r.Enabled = target; Save(r); }
            }
        }

        /// <summary>停用一份配方。</summary>
        public void Disable(string name)
        {
            var r = Get(name);
            if (r != null && r.Enabled) { r.Enabled = false; Save(r); }
        }

        /// <summary>文件名消毒（配方名可含中文/空格，只挡文件系统非法字符）。</summary>
        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
