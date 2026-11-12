# 2026年11月11日 重复查询预算验收

本人提交 budget-check 输出：三次 order-api 请求，前两次准许执行，第三次拒绝，并打印 `PASS: 重复查询预算检查`。该用户输出未单独显示退出码；助手随后从当前源码构建并复现，确认退出码。

## 本人完成的修改

- 通过 AIFunctionFactory.Create 包装同一个 ServiceTools 实例，再通过 InvokeAsync 发起三次调用。
- 保存返回结果并检查两次成功、第三次超限、serviceId、maxToolExecutions 与 Used=2。
- 根据断言打印 PASS 或 FAIL，并分别返回 0 或 1。
- 将请求日志移到 TryConsume 前，拒绝请求也有对应的调用日志。

Used 继续遵守此前确认的近似观测契约，本轮没有重新要求 getter 加锁。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| 当前源码 Release 编译 | 成功，0 警告、0 错误 |
| 当前应用 budget-check | PASS，退出 0；请求/准许/拒绝日志计数为 3/2/1 |
| 临时副本边界错误 | 将预算条件从 `Used >= Limit` 改为 `Used > Limit`，产生三次准许；本人当前检查器输出 FAIL，退出 1 |

故障检查说明本人接入的断言现在能够识别此前漏检的第三次放行错误；这一项验收通过。它不证明所有预算场景或整个 Agent 循环都已经受控。

构建使用当前解决方案、锁定依赖和已有 `/tmp/maf-dotnet-home`、`/tmp/maf-nuget` 缓存。正常运行入口是 `dotnet apps/FirstAgent/bin/Release/net10.0/FirstAgent.dll budget-check`。

故障仅注入 `/tmp/maf-budget-verified-mutant-1akjkmm1` 中的应用副本，检查入口沿用本人当前源码，不换成助手的另一套断言。该临时项目清空远程包源，以锁定模式使用缓存，只对临时项目关闭包审计联网；输出与真实退出码保存在该目录的 `verification.log`。工作区应用代码和快照没有改动。

本轮未调用模型或 Magpie，未提交或推送，未重复执行上一轮临时检查器中的其他序列与并发测试。

## 下一步

将重复查询检查抽成接收名称、服务 ID 序列和预期结果的辅助方法，每组创建独立预算，组内共享同一实例。补齐更换 ID、未知服务失败消耗额度、新预算隔离和并发计数场景，并汇总所有检查的退出状态。任务单已有输入与期望结果。

复述问题：只有工具执行次数上限时，模型是否仍可能持续请求工具？这将作为下一小节模型轮数与任务终止的衔接问题。

## 多场景复用时的断言误判

本人将三次调用抽为 `BudgetChecking(string[] serviceIds)`，分别传入更换 ID 与未知服务在先的序列。组间各自创建新预算，并用 `results.All(...)` 汇总退出码，这两处符合练习要求。

但只参数化了输入，断言仍保留原单场景的两个固定条件：第一项 `ok` 必须为 true，第三项 serviceId 必须为 order-api。

| 第二组调用 | 实际返回 | 本场景应有预期 |
| --- | --- | --- |
| billing-api | ok=false，UNKNOWN_SERVICE | 预期的业务未找到结果，同时消耗一次额度 |
| order-api | ok=true，degraded | 成功，消耗第二次额度 |
| inventory-api | ok=false，TOOL_BUDGET_EXCEEDED，maxToolExecutions=2 | 超限拒绝，serviceId 应保留 inventory-api |

当前布尔表达式在首次 `ok=false` 时就因短路而判定失败；即使只修正这一项，末次 ID 的固定值也会使检查继续失败。这是检查器预期未参数化，不能据此认定预算控制错误。

助手从当前源码重新构建 Release，0 警告、0 错误；执行 budget-check 复现一项 PASS、一项 FAIL，总退出码 1。另对捕获的工具 JSON 逐项比较，确认第二组业务结果符合上述预期，且超限上限字段为 2。本轮未修改应用代码，未调用模型或 Magpie，未提交或推送。

修正方向：辅助方法同时接收用例名和 `string?[] expectedErrors`，约定 null 表示预期成功，非 null 表示必须失败且错误码匹配；服务 ID 与本次输入比较，不再固定为 order-api。保留额度上限与 Used 的断言，输出具体用例名称。当前工作区的修正尚待本人完成，隔离和并发用例仍未加入。

资料静态检查通过。整个工作区的 git diff --check 另提示 Program.cs 第 28 行存在尾随空白；它与本次断言误判无关，助手保持本人代码未改动。
