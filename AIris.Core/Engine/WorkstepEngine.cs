using System;
using AIris.Core.Judge;
using AIris.Core.Model;

namespace AIris.Core.Engine
{
    /// <summary>工步推进事件参数。</summary>
    public class StepEventArgs : EventArgs
    {
        public Recipe Recipe { get; set; }
        /// <summary>当前工步（1 起）。</summary>
        public int StepIndex { get; set; }
        public RecipeStep Step { get; set; }
        public override string ToString() { return "工步 " + StepIndex + " · " + (Step == null ? "" : Step.Name); }
    }

    /// <summary>判定结论事件参数（UI 引导卡/审计订阅）。</summary>
    public class VerdictEventArgs : EventArgs
    {
        public int StepIndex { get; set; }
        public RecipeStep Step { get; set; }
        public bool Pass { get; set; }
        /// <summary>稳定帧进度（当前连续一致帧 / 需求帧）。</summary>
        public int StableNow { get; set; }
        public int StableNeed { get; set; }
        /// <summary>本帧结论一句话（OK/NG detail）。</summary>
        public string Detail { get; set; } = "";
        /// <summary>已判定的最终结论（稳定帧满足后为 true 的那次回调）。</summary>
        public bool IsFinal { get; set; }
    }

    /// <summary>
    /// 工步状态机（调研报告 §6 架构"工步引擎"层）：
    /// 持配方 → 指向当前步 → RunMonitor 每帧喂 JudgeOutcome → 连续 StableFrames 帧一致才落结论
    /// （抗手部遮挡抖动，调研报告 §6.2 第 1 条）→ OK 推进下一步 / NG 挂起等人工重检。
    /// 线程模型：仅 RunMonitor 的检测线程调用 Feed*（单线程串行），事件在调用线程抛出（UI 侧自行调度）。
    /// </summary>
    public class WorkstepEngine
    {
        private Recipe _recipe;
        private int _index;              // 当前步（0 起）
        private bool _lastPass;          // 上一帧结论
        private int _stableNow;          // 连续一致帧数
        private bool _stepSettled;       // 本步已落最终结论（OK 推进/NG 挂起后不再累计）

        /// <summary>是否在运行（有配方且有步）。</summary>
        public bool IsRunning { get { return _recipe != null && _recipe.Steps != null && _recipe.Steps.Count > 0; } }

        /// <summary>当前步索引（1 起；无配方 = 0）。</summary>
        public int CurrentIndex { get { return IsRunning ? _index + 1 : 0; } }

        /// <summary>当前步定义。</summary>
        public RecipeStep CurrentStep { get { return IsRunning && _index < _recipe.Steps.Count ? _recipe.Steps[_index] : null; } }

        public Recipe CurrentRecipe { get { return _recipe; } }

        // —— 事件（RunMonitor 转 UI/审计） ——
        public event EventHandler<StepEventArgs> StepChanged;
        public event EventHandler<VerdictEventArgs> Verdict;
        public event EventHandler<StepEventArgs> AllDone;

        /// <summary>装载配方并从第 1 步开始（切换/启用配方时调用）。</summary>
        public void Reset(Recipe recipe)
        {
            _recipe = recipe;
            _index = 0;
            _lastPass = false;
            _stableNow = 0;
            _stepSettled = false;
            if (IsRunning) RaiseStep();
        }

        /// <summary>回退到指定步（重检/回跳；index 为 1 起序号）。</summary>
        public void Goto(int index)
        {
            if (!IsRunning) return;
            _index = Math.Max(0, Math.Min(_recipe.Steps.Count - 1, index - 1));
            _lastPass = false;
            _stableNow = 0;
            _stepSettled = false;
            RaiseStep();
        }

        /// <summary>
        /// 喂入本帧判定结论。稳定帧累计满足 → 落最终结论：OK 自动推进，NG 挂起。
        /// 返回 true 表示本帧产生了"最终结论"（UI/审计需要落档）。
        /// </summary>
        public bool Feed(JudgeOutcome outcome)
        {
            if (!IsRunning || outcome == null || _stepSettled) return false;
            int need = Math.Max(1, CurrentStep.Rule.StableFrames);

            // 连续一致帧累计：结论翻转则重新计
            if (outcome.Pass == _lastPass) _stableNow++;
            else { _stableNow = 1; _lastPass = outcome.Pass; }

            RaiseVerdict(outcome, need, false);

            if (_stableNow >= need)
            {
                _stepSettled = true;
                RaiseVerdict(outcome, need, true);   // 最终结论回调（审计落档）
                if (outcome.Pass)
                {
                    _index++;
                    if (_index >= _recipe.Steps.Count) { RaiseAllDone(); return true; }
                    _lastPass = false;
                    _stableNow = 0;
                    _stepSettled = false;
                    RaiseStep();
                }
                // NG：挂起在当前步，等 Goto 重检
                return true;
            }
            return false;
        }

        /// <summary>规则快照（审计 RuleSnapshot 口径："数量=4 · IoU≥0.35 · 稳定3帧"）。</summary>
        public static string RuleSnapshot(RecipeStep step)
        {
            var r = step.Rule;
            string qty = step.Expectation == StepExpectation.FinalCheck
                ? "齐套"
                : (r.ExpectedCount.HasValue ? "数量=" + r.ExpectedCount.Value : "数量≥1");
            return qty + " · IoU≥" + r.IoUThreshold.ToString("0.##") + " · 稳定" + Math.Max(1, r.StableFrames) + "帧"
                 + (r.DualChannel ? " · 双通道" : "");
        }

        private void RaiseStep()
        {
            var h = StepChanged;
            if (h != null) h(this, new StepEventArgs { Recipe = _recipe, StepIndex = CurrentIndex, Step = CurrentStep });
        }

        private void RaiseVerdict(JudgeOutcome o, int need, bool final)
        {
            var h = Verdict;
            if (h != null) h(this, new VerdictEventArgs
            {
                StepIndex = CurrentIndex,
                Step = CurrentStep,
                Pass = o.Pass,
                StableNow = _stableNow,
                StableNeed = need,
                Detail = o.Detail,
                IsFinal = final
            });
        }

        private void RaiseAllDone()
        {
            var h = AllDone;
            if (h != null) h(this, new StepEventArgs { Recipe = _recipe, StepIndex = _recipe.Steps.Count, Step = null });
        }
    }
}
