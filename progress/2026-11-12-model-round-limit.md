# 2026年11月12日 模型调用循环上限开课

用户要求进入下一阶段。上一阶段已通过 PR #1 合并，旧功能分支已删除；本轮从 main 的 b577680 开始，创建 `feat/model-round-limit` 承接课程材料和后续练习。

## 当前目标与所有权

本小节目标是在 MAF 自动工具循环中配置上限，并用底层 IChatClient 的实际调用数验证边界。先完成 [任务一：配置现有循环](../lessons/02-model-round-limit.md#任务一-配置现有框架循环)，再接入离线 loop-check。宿主终止状态的建模放在其后，不一次要求实现全部能力。

本轮应用代码未修改。任务单、知识卡片和独立临时实验由助手准备；用户尚未实现模型轮数配置或新检查入口，不记为已经掌握。既有工具预算实现与四项检查属于上一阶段成果。

## 版本核对

本次锁文件与本机缓存均为 MAF 1.27.0、Microsoft.Extensions.AI 10.10.0、Microsoft.Extensions.AI.Abstractions 10.10.1、Microsoft.Extensions.AI.OpenAI 10.10.1 和 OpenAI 2.14.0。当前包缓存位于 `/home/leo/.nuget/packages`，此前记录的 `/tmp/maf-nuget` 本轮不存在，没有沿用旧环境假设。

Extensions 包的 nuspec 标注源码提交 `02107c65bab30aad9e35b5133ed643eaa77bccd8`。助手读取该固定提交的 FunctionInvokingChatClient 源码并对照本地 XML 文档。普通入口从 iteration=0 开始，在达到上限后仍调用一次内层客户端，但移除函数工具且不再执行返回的函数调用；流式入口有同样边界。

本地上游目录 HEAD 当前为 `22b5e752ba71be3421391ccab4a92631f030aacb`，与课程初始化基线不同。没有修改、拉取或切换该目录。本轮通过 `git show dotnet-1.27.0:路径` 阅读 MAF 标签，标签解析到 `4d1674b78951d6fd127ada532660514637f906e9`；并用实际安装包验证 clientFactory 入口，不假设 HEAD 与应用一致。

官方在线属性文档描述了默认值 40，并说包含首次请求；实际版本的最后请求边界需结合源码与实验，不能仅凭这段描述把配置 2 当成严格两次模型调用。

## 助手独立确定性实验

临时项目：`/tmp/maf-round-semantics-8fa7v2cq/RoundProbe.csproj`。依赖版本与应用锁文件一致；包源清空，使用本地缓存，只有这个临时项目关闭 NuGet 审计联网，应用配置未改变。

使用 Responses AsAIAgent 的 clientFactory 替换底层客户端，返回 `FunctionInvokingChatClient(脚本客户端)`，MaximumIterationsPerRequest=2。SDK 对象使用离线占位值，底层网络客户端被替换，没有发起 HTTP、Magpie 或真实模型请求。

脚本看到函数工具时返回一个名为 Probe 的调用，工具固定正常返回 ok=false、TOOL_BUDGET_EXCEEDED；函数工具移除后返回 scripted-final。脚本还设置了大于 6 次调用即抛异常的实验保护。

还原命令主体：`dotnet restore /tmp/maf-round-semantics-8fa7v2cq/RoundProbe.csproj --locked-mode --configfile /tmp/maf-round-semantics-8fa7v2cq/NuGet.Config --tl:off`，退出 0。

运行命令主体：`dotnet run --project /tmp/maf-round-semantics-8fa7v2cq/RoundProbe.csproj --configuration Release --no-restore --tl:off`，编译并运行成功，退出 0。

```text
PASS: once; limit=2; modelCalls=3; toolCalls=2; tools=[1,1,0]; text=scripted-final
PASS: stream; limit=2; modelCalls=3; toolCalls=2; tools=[1,1,0]; text=scripted-final
```

两种入口分别创建实例并断言以上计数、工具列表及最终文字。结果证明了实际依赖的循环边界，以及业务错误 JSON 不会自行终止循环；不证明真实模型一定会在最后请求中总结，也不证明用户应用已接入配置。临时项目不属于当前应用的 CI 检查。

## 本人下一步

先在 Program.cs 的现有 Responses AsAIAgent 调用上接入 clientFactory，固定 MaxToolRounds=2，编译并运行既有 budget-check。提交修改与输出，再实现任务单中的离线 loop-check。一个失败实验是将配置改为 1、保留原断言，确认检查器能发现调用次数变化。

知识卡片：[预算作用域、计数位置与终止](../knowledge/04-budgets-and-termination.md)。课程资料静态检查与差异检查通过；这与上面的临时项目运行结果分别记录，不代表用户练习验收通过。本轮没有提交或推送新分支。
