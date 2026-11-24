# 2026年11月24日 课程评审与第 2 课收口

## 结论

本人提出：学习一到两周后，感觉没有深入了解多少 Agent Harness 开发技术。助手评审后的结论是原路线确实偏离，分三层：

- **目标没对准**：`docs/roadmap.md` 把 harness 和上下文压缩列为第二阶段选修。已安装的 MAF 1.27.0 自带 `HarnessAgent`、`Compaction/`、`Loop/`、各类上下文 Provider 和官方示例 `dotnet/samples/02-agents/Harness/`，学习材料对这些类型的引用为 0。
- **顺序靠后**：自研循环排在 D02，前面是第一阶段 64–96 小时的应用交付。11-06 至 11-23 共 10 个学习日、16 份记录，仍在 L02 收尾。
- **深度用偏**：11-10 到 11-23 的 11 份记录都围绕 `FunctionInvokingChatClient`（属于 Microsoft.Extensions.AI）的预算与终止。应用代码约 540 行，学习材料约 3,400 行；循环本身没有写过，11-09 之后的记录均写明未调用真实模型。

保留：L01–L02 的 provider、instructions、函数工具基础，以及 `ScriptedChatClient`、`ObservedChatClient`、`ToolCallBudget`。旧课程、记录、知识卡片和反思笔记不删除。

## 本人的决定

1. 以自己写为主，MAF 的 Harness 实现作对照；原故障排查应用只作测试场景。
2. L02 收口后转向，RAG、工作流、审批、托管降为选修。
3. 每周投入 5 小时以内。
4. 记录精简，保留两条底线：未验证必须标注；助手写的不等于本人已掌握。

## 本次改动

- 删除 `Program.cs` 里没写完的空函数 `ClassifyRun`。`RunOutcome.cs` 保留。
- [宿主终止结果任务](../lessons/02-run-outcome.md) 标记为搁置。
- 后续由助手重排路线并准备 H01（自研 Agent 循环）任务单。

## 证据

助手执行（2026-11-24，确定性检查与静态检查）：删除空函数后 Release 构建 0 警告 0 错误；`budget-check` 4/4 通过、退出 0；四组 `loop-check` 均 PASS、退出 0；`validate-learning.py` 通过。未调用真实模型。
