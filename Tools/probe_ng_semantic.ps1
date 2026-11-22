# NG 语义补验（探针，UTF-8 BOM + CRLF）：清空场景 → 重新检测 → 判定应 NG。
# 临时探针，验证完可删；运行：powershell -File Tools\probe_ng_semantic.ps1
$ErrorActionPreference = 'Stop'
$bin = 'E:\Project\AIris\00_ExeBuild'
Add-Type -Path "$bin\Newtonsoft.Json.dll"
Add-Type -Path "$bin\AIris.Core.dll"

$scene = [AIris.Core.Detect.MockScene]::Instance
$scene.Clear()
$engine = New-Object AIris.Core.Detect.MockDetectionEngine
$prompts = New-Object 'System.Collections.Generic.List[string]'
$prompts.Add('金属垫板')

$step = New-Object AIris.Core.Model.RecipeStep -Property @{ Name='放置金属垫板'; ExpectedPart='金属垫板'; Expectation=[AIris.Core.Model.StepExpectation]::Appear }
$roi  = New-Object AIris.Core.Model.ROIRect  -Property @{ Id='ROI-A'; Name='台面区'; X=80; Y=130; W=400; H=380 }

# 在场 → OK
$null = $scene.PlacePart('金属垫板')
$d1 = $engine.DetectAsync($null, $prompts, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
$parts = New-Object 'System.Collections.Generic.List[string]'
$parts.Add('金属垫板')
$o1 = [AIris.Core.Judge.JudgeEngine]::Judge($step, $roi, $d1.Detections, $parts)
Write-Output ("在场判定: Pass=" + $o1.Pass + " detail=" + $o1.Detail)

# 拿走 → NG（模拟少装：检出数 < 预期，重新检测取新框）
$null = $scene.RemovePart('金属垫板')
$d2 = $engine.DetectAsync($null, $prompts, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
$o2 = [AIris.Core.Judge.JudgeEngine]::Judge($step, $roi, $d2.Detections, $parts)
Write-Output ("拿走后判定: Pass=" + $o2.Pass + " detail=" + $o2.Detail)

if ($o1.Pass -and -not $o2.Pass) { Write-Host 'NG-SEMANTIC-OK' -ForegroundColor Green }
else { throw 'NG 语义未生效' }
