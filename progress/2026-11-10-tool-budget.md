# 2026年11月10日 工具预算类审查

本人在 `feat/tool-call-budget` 分支新增 [ToolCallBudget.cs](../apps/FirstAgent/ToolCallBudget.cs)，提交状态为未跟踪文件。助手只读审查该类，并在临时项目中验证，没有替本人修改应用代码，也没有提交或推送变更。

## 实现评估

构造函数拒绝非正上限，Limit 为只读属性。TryConsume 使用同一把锁包住额度检查与计数递增，耗尽后返回 false 且不再递增。状态保存在实例中，符合本课单次运行共享预算的设计方向。

首轮助手将 Used getter 未加锁列为待修正项。用户随后指出：Used 只作计数快照，即使 getter 加锁，返回后仍可能被其他线程消耗额度，后续 TryConsume 依然可能失败。这个反例成立。

讨论后的契约：Used 只供近似观测，允许过时，不承诺刷新时限；不得用于取得额度或轮询决定任务结束。唯一准入依据是 TryConsume 的返回值。按此契约，撤回 getter 必须加锁的验收要求，当前预算类可继续接入工具入口。

getter 加锁能加强读取与写入的同步，但不会将一次读取与稍后的 TryConsume 合并为同一原子操作。int 单次读写原子，不代表值一定最新，也不代表检查与递增天然原子；后者已由 TryConsume 中的锁保护。锁的开销和竞争属于设计取舍，本轮没有基准测试，不作显著性能损失结论。

本次对照了 [C# 变量原子性规范](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/variables#96-atomicity-of-variable-references)、[lock 语句规范](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/statements#1313-the-lock-statement) 和 [Volatile 的内存顺序说明](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile?view=net-10.0#remarks)。Volatile 也不承诺读取全局最新值，不能用它替代额度占用操作；本课不因此引入额外同步实现。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| 应用 Release 编译 | 成功，0 警告、0 错误 |
| limit 为 0、-1、int.MinValue | 均抛 ArgumentOutOfRangeException，参数名为 limit |
| 上限 1 与 2 的顺序消费 | 正确准许；耗尽后拒绝，重复拒绝 100 次仍不增加 Used |
| 实例隔离 | 新实例获得独立额度，原实例保持耗尽状态 |
| 并发调用样本 | 100 轮，每轮 20 次 Parallel.For 调用，上限 2；每轮恰好成功 2 次，Used 为 2 |

编译命令：`dotnet build apps/apps.slnx --configuration Release --no-restore --tl:off`，使用已有 `/tmp/maf-dotnet-home` 和 `/tmp/maf-nuget` 缓存环境。

独立检查项目位于 `/tmp/maf-budget-review-wlv8sxfp`，通过 Compile Link 引用本人实际的 ToolCallBudget.cs，没有复制另一份实现。它不依赖额外 NuGet 包，NuGet.Config 清空远程包源；执行 `dotnet run --project /tmp/maf-budget-review-wlv8sxfp/BudgetReview.csproj --configuration Release --tl:off` 退出码为 0。

并发检查在工作线程完成后读取 Used，没有验证并发写入期间的观测新鲜度；当前契约也不承诺这种新鲜度。上述检查只覆盖预算对象，不证明 ServiceTools、MAF 工具调度、模型轮数或整个任务终止已受控制。本轮没有发起模型请求。后续契约讨论只修改文档，没有修改应用代码，因此没有重复编译或执行这些检查。

## 预算类审查后的任务

本人按 [任务二](../lessons/02-tool-budget.md#任务二-在工具入口实施限制) 改造 ServiceTools：通过构造函数持有预算，在读取快照前调用 TryConsume 并依据返回值决定是否执行，Program.cs 注册该实例的方法。之后用 budget-check 验证第三次调用拒绝、未知服务消耗额度及不同运行隔离。

## 工具入口接入后的审查

本人随后完成 ServiceTools 实例化，构造函数接收预算；Program.cs 创建一个上限为 2 的预算并注册该实例的 GetServiceHealth。函数先调用 TryConsume，再校验参数与读取快照。当前 CLI 每次进程只执行一次顶层运行，这个对象生命周期符合本课要求。

Release 编译成功，0 警告、0 错误。助手没有修改本人应用代码；独立检查使用临时项目 `/tmp/maf-budget-entry-review-w4f3rfs8`，通过 Compile Link 引用工作区实际 ToolCallBudget.cs 和 ServiceTools.cs，使用当前锁定依赖，再经 AIFunctionFactory.Create 和 AIFunction.InvokeAsync 调用。不是通过模型请求驱动的 Agent 运行。

临时项目使用已有 `/tmp/maf-nuget` 缓存、清空远程包源，并只在该临时项目运行时设置 `NuGetAudit=false`；原应用审计设置未改动。执行命令主体为 `dotnet run --project /tmp/maf-budget-entry-review-w4f3rfs8/BudgetEntryReview.csproj --configuration Release -p:NuGetAudit=false --tl:off`。检查汇总为 8 项通过、8 项失败，退出码为 1；失败项来自四组序列各自的结果契约和日志约定。

| 场景 | 核心拦截结果 |
| --- | --- |
| 连续三次 order-api | 前两次成功，第三次拒绝，Used=2 |
| order-api、inventory-api、order-api | 更换参数不重置预算，第三次拒绝 |
| billing-api、order-api、inventory-api | 未知服务消耗额度，第三次拒绝 |
| 空字符串、order-api、inventory-api | 进入函数后的参数校验失败消耗额度，第三次拒绝 |
| 耗尽后使临时输出目录的快照文件缺失 | 仍返回预算拒绝；对照组的新预算调用到达文件读取并抛 FileNotFoundException |
| 新建预算与工具实例 | 第一次查询成功，旧预算仍耗尽 |
| 同一包装工具并发 20 次 | 2 次成功、18 次预算拒绝，Used=2 |

文件缺失实验只移动临时项目输出目录中的副本，并在 finally 中还原；工作区的快照文件未改动。日志保存在临时项目输出目录的 `review-tool-log.txt`。

核心检查识别的是当前实现实际使用的 `TOOL_CALL_BUDGET_EXCEEDED`，并另设检查与任务单约定比对，未将错误码差异掩盖为整体通过。

尚需修正的两类差异：

1. 任务单与 T04 约定 `TOOL_BUDGET_EXCEEDED`；当前多了 CALL，并且超限结果缺少 serviceId 和 maxToolExecutions。错误码命名本身没有内建对错，但实现、调用方和评测必须使用一致契约。
2. `[tool-call]` 位于 TryConsume 之后，被拒绝的请求不会产生该日志；同时缺少 `[tool-admitted]` 与 `[tool-rejected]`。每组三次调用的实际日志计数为 2/0/0，约定为 3/2/1。

以上验证证明了本次工具包装调用中的执行额度限制，不证明模型轮数、整次任务终止或 CI 自动回归已经完成。本轮没有调用模型或 Magpie。应用尚不支持 budget-check 模式。

## 工具入口审查后的任务

本人统一超限错误结果、补齐请求与准入日志，然后按任务三将确定性检查加入 budget-check 模式。继续保留 Used 的近似观测契约，不通过外部读取 Used 决定准入。

## 本人提交 budget-check 演示

本人提交了 budget-check 的终端输出：前两次 order-api 查询成功，第三次返回 TOOL_BUDGET_EXCEEDED，包含 serviceId 和 maxToolExecutions。源码已修正超限契约，并在 TryConsume 中增加准许与拒绝日志；请求日志仍在准入检查之后，实际请求/准许/拒绝计数为 2/2/1，约定为 3/2/1。

Program.cs 在模型客户端创建之前处理 budget-check，直接调用同一 ServiceTools 实例三次。这个位置避免了模型请求，但返回值被丢弃，之后直接 return 0；尚未通过 AIFunction 包装调用，也没有结果断言。

助手重新编译 Release，0 警告、0 错误；运行该模式得到与本人一致的两次成功和一次拒绝，退出码为 0。

## 验证检查器能否发现故障

为验证检查器本身，在 `/tmp/maf-budget-check-mutant-qein64p7` 复制当前应用及虚构快照，只将副本中预算条件从 `Used >= Limit` 改为 `Used > Limit`，模拟多准许一次的边界错误。工作区应用未改动。

故障副本的三次查询都成功，Used 达到 3，但原 budget-check 仍以 0 退出。这证明当前演示不能自动识别该类回归，而不是说明工作区预算实现存在这个边界错误。

随后在独立临时目录 `/tmp/maf-budget-check-example-b2yw8lvy` 验证任务单中的单场景断言示例：保存 AIFunction.InvokeAsync 结果，检查两次成功、第三次超限及结果字段，并检查 Used=2。

| 版本 | 实际观测 | 检查器表现 |
| --- | --- | --- |
| 本人实现 + 本人演示入口 | 两次成功、第三次拒绝 | 退出 0，尚无自动断言 |
| 故障副本 + 本人演示入口 | 三次都成功，Used=3 | 仍退出 0，漏检故障 |
| 正常实现 + 助手断言示例 | 两次成功、第三次拒绝 | 打印 PASS，退出 0 |
| 同样故障 + 助手断言示例 | 三次都成功，Used=3 | 打印 FAIL，退出 1，成功识别故障 |

临时项目使用锁定依赖和已有 NuGet 缓存，包源清空；只对临时项目关闭审计联网，原应用设置保持不变。示例目录的预算源码在验证后恢复为正常实现；故障演示留在单独的 mutant 目录。断言示例尚未由本人接入工作区，不记为本人已完成自动验证。

本轮没有调用模型或 Magpie；没有替本人修改应用代码、提交或推送。未重新执行完整并发与多序列检查，已有历史结果的范围不扩大。

## 当前下一步

本人先将三次调用演示改成能判定 PASS/FAIL 的检查，使用 AIFunction 包装，并将请求日志移到 TryConsume 前；然后补齐任务单中的更换 ID、失败扣额、新预算隔离和并发场景。预期业务拒绝出现应使测试通过，错误放行才应使测试失败。
