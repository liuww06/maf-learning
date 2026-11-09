# Microsoft Agent Framework 学习与自研实践

[![CI](https://github.com/liuww06/maf-learning/actions/workflows/ci.yml/badge.svg)](https://github.com/liuww06/maf-learning/actions/workflows/ci.yml)

目标：先用 C# / .NET 和 Microsoft Agent Framework（MAF）完成可验证的 AI 应用，再理解其关键实现、开发自己的 Agent 框架，并形成能够用于技术面试的项目证据和技术表达。

学习主线已经确认使用 **C# / .NET**。课程围绕“研发知识与故障排查助手”持续演进；每次学习都经过“理解问题 → 实现一个能力 → 观察执行过程 → 注入失败 → 解释设计 → 记录证据”。

## 从这里开始

1. 阅读 [两阶段学习路线](docs/roadmap.md)，了解阶段成果和进入下一阶段的条件。
2. 阅读 [主线应用设计](docs/project-brief.md)，理解要解决的具体问题。
3. 完成 [第 0 课：环境与 Agent 执行模型](lessons/00-baseline.md)。本课不需要模型 API。
4. 根据 [第 1 课任务单](lessons/01-first-agent.md) 开始第一个 MAF 应用。
5. [第 2 课：注册函数工具](lessons/02-function-tools.md) 已完成两个基本场景的第二轮验证，当前进行 [工具执行预算](lessons/02-tool-budget.md)。

## 克隆与运行

安装 .NET SDK 10.0.403 或同一功能带的更新补丁，具体选择规则见 [global.json](global.json)。

```bash
git clone https://github.com/liuww06/maf-learning.git
cd maf-learning
dotnet restore apps/apps.slnx --locked-mode
dotnet build apps/apps.slnx --configuration Release --no-restore
python3 scripts/validate-learning.py
```

完成构建后，启动本机 Magpie 网关，再运行：

```bash
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- stream "请调用工具查询 order-api 的快照状态，并给出观测时间和证据编号。"
```

该命令会调用实际模型服务。网关地址默认 `http://127.0.0.1:3425/v1`，模型和认证通过环境变量配置，详见 [应用说明](apps/FirstAgent/README.md)。服务快照是虚构学习数据。

## 提交与检查

主分支使用 `main`。初始化后通过功能分支和 Pull Request 合并变更，要求 `build-and-validate` 检查通过；对应 GitHub ruleset 要求 PR、分支与主线保持最新，并禁止删除或强制推送主分支。个人学习仓库不强制额外审批人。

CI 按锁文件还原依赖、执行 Release 编译，并检查 JSON、仓库内文档链接与评测资料。当前没有自动化测试项目，CI 不调用模型或本机网关；编译和静态检查不能替代模型行为评测。调用预算练习仍待实现。

## 可选的上游源码阅读

构建应用不需要克隆上游。需要本地源码对照时，在仓库根目录单独克隆：

```bash
git clone https://github.com/microsoft/agent-framework.git agent-framework
git -C agent-framework switch --detach 1c3264b065ef4b2b9b3614b19394bdd49d98334f
```

以上切换仅用于新克隆的源码参考目录。`agent-framework/` 已忽略，不作为本学习仓库的子模块提交。阅读地图链接固定到课程初始源码基线，应用依赖仍以 NuGet 锁文件为准；两者不保证完全相同。

## 学习资料

| 资料 | 用途 |
| --- | --- |
| [知识地图](knowledge/00-map.md) | 把模型基础、Agent 运行时、检索和工程治理串起来 |
| [源码阅读地图](source-notes/00-reading-map.md) | 带着应用问题追踪真实调用链 |
| [学习进度](progress/tracker.md) | 区分材料已准备、代码已验证和本人已掌握 |
| [首次学习记录](progress/2026-11-06.md) | 保存环境基线、决定和下一步 |
| [面试训练题](interviews/question-bank.md) | 从定义、机制、取舍到项目证据组织回答 |
| [课后记录模板](templates/session.md) | 每课整理实验、失败和复盘 |
| [知识卡片模板](templates/knowledge.md) | 保存自己的解释、反例和依据 |
| [设计决策模板](templates/adr.md) | 记录自研框架的方案与取舍 |
| [练习数据说明](fixtures/README.md) | 使用明确标注的虚构业务资料 |
| [起始评测用例](evals/starter-cases.json) | 从第一天建立可检验的行为要求 |

## 当前基线

核对日期：2026-11-06。

- 本机 .NET SDK：`10.0.403`；运行时：`10.0.14`。
- 本地源码：`agent-framework/`，提交 `1c3264b065ef4b2b9b3614b19394bdd49d98334f`。
- `git describe`：`dotnet-1.27.0-92-g1c3264b06`。这是源码相对标签的位置，**不是应用已经安装的 NuGet 版本**。
- 源码 `dotnet/global.json` 指定 SDK `10.0.401`，允许 `minor` roll-forward；本机 SDK 版本满足该选择策略。构建能力仍需实际编译验证。
- 已创建 [FirstAgent](apps/FirstAgent/README.md)，锁定 MAF 1.27.0；经 Magpie 的 Responses 普通与流式模式各验证一次。模型其他能力和费用预算待确认。
- 开发背景、每周学习时间、目标岗位细分待补充，课程暂不绑定日历日期。

第一阶段的应用放在 `apps/`，采用经验证并锁定的 NuGet 依赖。第二阶段的自研实现将在课程推进到该阶段时创建于 `framework/`。`agent-framework/` 用于源码对照，保留其原有仓库和构建规则。

官方文档介绍了 Agent、工作流、会话、上下文提供器和中间件等主要能力；各语言、模型提供方和发布版本的能力应分别核对。[Microsoft Learn 概览](https://learn.microsoft.com/en-us/agent-framework/overview/)

## 我们如何协作

每课由助手提供目标、必要解释、最小任务和验收方式，你完成关键实现或修改并解释结果；需要时助手示范、排错和审查。每次结束更新学习记录和进度。若你明确要求完整实现，助手按要求完成，但“助手完成代码”与“你已掌握”分别记录。

掌握的标准是：你能独立修改需求、定位一次故障、解释关键取舍，并在另一场景复用能力。阅读完文档或成功运行一次示例，只是其中的一部分。
