# Agent Harness 学习与自研实践（MAF 对照）

[![CI](https://github.com/liuww06/maf-learning/actions/workflows/ci.yml/badge.svg)](https://github.com/liuww06/maf-learning/actions/workflows/ci.yml)

目标：用 C# / .NET 自己实现一个 Agent Harness，以 Microsoft Agent Framework（MAF）的 Harness 实现为对照，并形成能够用于技术面试的项目证据和技术表达。

学习主线是 **C# / .NET**。2026-11-24 起以自研 Harness 为主线（`framework/`），原“研发知识与故障排查助手”保留为测试场景。每课经过“自己写最小实现 → 脚本模型离线检查 → 真实模型运行 → 读 MAF 对应实现”。

## 从这里开始

1. 阅读 [学习路线](docs/roadmap.md)，了解 H01–H10 的安排和节奏。
2. 阅读 [Harness 阅读地图](source-notes/01-harness-reading-map.md)，知道每课对照读哪些 MAF 源码。
3. 按 [H01：自研 Agent 循环](lessons/H01-agent-loop.md) 开始。项目骨架已备好，循环由你实现。
4. 第 0–2 课已完成并收口：[基线](lessons/00-baseline.md)、[第一个 Agent](lessons/01-first-agent.md)、[函数工具](lessons/02-function-tools.md)、[工具执行预算](lessons/02-tool-budget.md)、[模型循环上限](lessons/02-model-round-limit.md)、[请求观测](lessons/02-run-observation.md)。

## 克隆与运行

安装 .NET SDK 10.0.403 或同一功能带的更新补丁，具体选择规则见 [global.json](global.json)。

```bash
git clone https://github.com/liuww06/maf-learning.git
cd maf-learning
dotnet restore apps/apps.slnx --locked-mode
dotnet build apps/apps.slnx --configuration Release --no-restore
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- budget-check
python3 scripts/validate-learning.py
```

完成构建后，启动本机 Magpie 网关，再用对照应用发起一次真实调用：

```bash
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- stream "请调用工具查询 order-api 的快照状态，并给出观测时间和证据编号。"
```

该命令会调用实际模型服务。网关地址默认 `http://127.0.0.1:3425/v1`，模型和认证通过环境变量配置，详见 [应用说明](apps/FirstAgent/README.md)。服务快照是虚构学习数据。

H01 的实验宿主是 `apps/HarnessLab`：`check` 运行离线检查，`run "<问题>"` 用自研循环调用真实模型，用法见 H01 任务单。

## 提交与检查

主分支使用 `main`。通过功能分支和 Pull Request 合并变更，要求 `build-and-validate` 检查通过；对应 GitHub ruleset 要求 PR、分支与主线保持最新，并禁止删除或强制推送主分支。个人学习仓库不强制额外审批人。

CI 按锁文件还原依赖、执行 Release 编译、运行 `budget-check`，并检查 JSON、仓库内文档链接与评测资料。当前没有独立测试项目，CI 不调用模型或本机网关；确定性检查不能替代模型行为评测。`loop-check` 与 `loop-check-stream` 已由本人手工运行验收，尚未加入 CI。

CI 在向 `main` 推送、向 `main` 发起 PR 或手动触发时运行；单独推送功能分支不会自动触发。

## 可选的上游源码阅读

构建应用不需要克隆上游。需要本地源码对照时，在仓库根目录单独克隆：

```bash
git clone https://github.com/microsoft/agent-framework.git agent-framework
```

`agent-framework/` 已忽略，不作为本学习仓库的子模块提交。应用依赖以 NuGet 锁文件为准。阅读地图的行号对应标签 `dotnet-1.27.0`，用 `git -C agent-framework show dotnet-1.27.0:<路径>` 读取，不需要切换工作区。

## 学习资料

| 资料 | 用途 |
| --- | --- |
| [学习路线](docs/roadmap.md) | H01–H10 的能力、对照和节奏 |
| [Harness 阅读地图](source-notes/01-harness-reading-map.md) | 每课对照阅读的 MAF 入口文件 |
| [知识地图](knowledge/00-map.md) | 把模型基础、Agent 运行时、检索和工程治理串起来 |
| [个人思考](reflections/README.md) | 保存真实疑问、认识变化和工程判断 |
| [源码阅读地图](source-notes/00-reading-map.md) | Agent、会话与 Workflow 的基础阅读路线 |
| [学习进度](progress/tracker.md) | 区分骨架已准备、代码已验证和本人已掌握 |
| [首次学习记录](progress/2026-11-06.md) | 保存环境基线、决定和下一步 |
| [面试训练题](interviews/question-bank.md) | 从定义、机制、取舍到项目证据组织回答 |
| [课后记录模板](templates/session.md) | 每次课的记录，30 行以内 |
| [知识卡片模板](templates/knowledge.md) | 保存自己的解释、反例和依据 |
| [设计决策模板](templates/adr.md) | 记录自研框架的方案与取舍 |
| [练习数据说明](fixtures/README.md) | 使用明确标注的虚构业务资料 |
| [起始评测用例](evals/starter-cases.json) | 原应用的 12 条种子用例，选修时使用 |

## 当前基线

核对日期：2026-11-24。

- .NET SDK 由 `global.json` 固定为 `10.0.403`。
- 应用依赖 MAF 1.27.0，见 [FirstAgent](apps/FirstAgent/README.md) 与锁文件；已通过 Magpie 的 Responses 普通与流式模式各验证过一次真实调用。
- 本地源码 `agent-framework/` 的提交是 `e2f52bc5a0c4b5e26901f63102fe509e51b48a92`。该目录在 11-06 克隆（提交 `1c3264b06`）后，于 11-09、11-12、11-16 三次 pull；早先文档里的 `1c3264b06` 不再是当前状态。
- 与应用版本精确对照时读标签 `dotnet-1.27.0`。它与当前工作区在 Harness 相关目录有 20 个文件不同，清单见 Harness 阅读地图。
- 每周投入 5 小时以内（本人 2026-11-24 确认）；开发背景待本人确认。模型其他能力和费用预算待确认。

对照应用在 `apps/FirstAgent`（保持现状），自研实现在 `framework/MiniHarness`，实验宿主在 `apps/HarnessLab`。`agent-framework/` 用于源码对照，保留其原有仓库和构建规则。

官方文档介绍了 Agent、工作流、会话、上下文提供器和中间件等主要能力；各语言、模型提供方和发布版本的能力应分别核对。[Microsoft Learn 概览](https://learn.microsoft.com/en-us/agent-framework/overview/)

## 我们如何协作

每课由助手给出任务、验收标准和项目骨架，你完成关键实现并解释结果；需要时助手示范、排错和审查。你明确要求完整实现时，助手按要求完成，但“助手完成代码”与“你已掌握”分别记录。每次结束更新进度，课后记录控制在 30 行以内：做了什么、谁验证了什么、下一步。

讨论中有价值的原理问题整理到 [个人思考模块](reflections/README.md)，同主题持续完善；助手归纳的结论标注“助手整理”。首篇是 [为什么 Agent 需要工具总调用预算](reflections/001-tool-call-budget.md)。

掌握的标准是：你能独立修改需求、定位一次故障、解释关键取舍，并在另一场景复用能力。阅读完文档或成功运行一次示例，只是其中的一部分。
