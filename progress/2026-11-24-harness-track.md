# 2026年11月24日 路线重排与 H01 骨架

## 做了什么

- 助手按 [评审结论](2026-11-24-curriculum-review.md) 重排学习路线为“自研 Agent Harness 主线”（H01–H10），重写 [学习路线](../docs/roadmap.md)、进度表、README 和知识地图入口，新增 [Harness 阅读地图](../source-notes/01-harness-reading-map.md)。
- 按本人选择“精简但保留底线”，修改 `AGENTS.md` 的教学与记录约定，同步课后记录模板、个人思考模板和索引。旧课程、记录、知识卡片和两篇反思正文没有删除。
- 新增 `framework/MiniHarness`（类库）和 `apps/HarnessLab`（实验宿主），加入解决方案；`RunOutcome.cs` 从 FirstAgent 移入 MiniHarness。
- 助手写的代码：两个项目文件、`AgentLoopOptions`、`AgentLoop.RunAsync` 的空壳（抛 `NotImplementedException`）、HarnessLab 的 `run` 分支与配置解析。`RunOutcome` 的契约由本人在 11-23 定义，助手只改了命名空间并补充字段注释。
- 新增 [H01 任务单](../lessons/H01-agent-loop.md)。

## 验证

确定性检查与静态检查，执行者为助手：

- Release 构建整个解决方案：0 警告 0 错误。
- 两个新项目还原成功并生成锁文件；解决方案 `--locked-mode` 还原通过；FirstAgent 的锁文件没有变化。
- HarnessLab：`check` 返回 1 并提示未实现；非法模式和非法地址返回 2；`run` 在发出模型请求前抛出 `NotImplementedException`。
- FirstAgent 在迁移后复验：`budget-check` 4/4，四组 `loop-check` 均 PASS。
- 学习资料静态检查通过。未调用真实模型。

## 未完成

- H01 的循环、`ScriptedModel` 和离线检查由本人实现，尚未开始。
- HarnessLab 的 `check` 尚未接入 CI。

## 同日更新

本人合并了 [PR #2](https://github.com/liuww06/maf-learning/pull/2)（提交 263fbeb）。助手把本地 `main` 快进到 263fbeb，并把本分支变基到 `main` 之上，之后重新跑了构建、FirstAgent 五项检查和静态检查，均通过。本人确认 `AGENTS.md` 的改动后，由助手推送本分支并发起面向 `main` 的 PR。
