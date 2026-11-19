# 2026年11月18日 FinishReason 对照验收

## 本人提交的运行证据

| 入口 | 场景 | Calls | Used | 函数声明数量 | 最终文字 | FinishReason | 结果 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| loop-check | 默认 | 3 | 2 | `[1,1,0]` | 脚本最终说明 | stop | PASS |
| loop-check | early | 2 | 1 | `[1,1]` | 脚本正常结束 | stop | PASS |
| loop-check-stream | 默认 | 3 | 2 | `[1,1,0]` | 脚本最终说明 | stop | PASS |
| loop-check-stream | early | 2 | 1 | `[1,1]` | 脚本正常结束 | stop | PASS |

以上来自本人提交的终端输出；本次未附构建输出或单独的退出码，不记为新增构建验证或退出码实测。源码在 passed 为 true 时返回 0。四组现有断言及 FinishReason 对照已验收，不要求仅为补截图重复运行。

## 静态核对与结论

助手确认 Program.cs 在场景三元断言之后执行公共 `passed &= response.FinishReason == ChatFinishReason.Stop;`，覆盖两种入口、两种场景，并打印 finish。ScriptedChatClient 的 early 分支仍根据输入中的 FunctionResultContent 返回，流式路径复用响应构造、按响应计数。

在当前锁定版本与管线中，默认场景走到移除函数声明的最后请求，early 场景在工具仍可用时结束；两者聚合后的 FinishReason 都是 Stop。这说明该字段不足以区分是否触达框架轮数上限，更不能独自证明业务任务成功。

本次验收是本人应用中的确定性实验，助手未重复执行应用或访问 Magpie。它不验证真实模型是否会自主提前结束、HTTP 流式行为或生产服务状态。记录为“对照实验已完成”；宿主终止结果仍未实现，机制解释仍待本人复述。

## 下一小步

按 [宿主结束原因的观测基础](../lessons/02-run-observation.md) 编写通用 ObservedChatClient，在 FunctionInvokingChatClient 内部、脚本客户端外部记录每次请求。将宿主断言的数据来源改为包装层，使观测不依赖测试替身内部属性，为以后接入真实客户端及构造宿主终止结果提供依据。

本次准备任务说明，未替本人实现包装层。沿用现有四组场景验证新包装位置，不再增加重复的轮数变更实验；本小节的失败实验改为把观测层故意放到循环外部，观察漏计。

## 资料检查

已同步课程、进度、应用说明及个人知识笔记中的验收状态。`python3 scripts/validate-learning.py` 通过：5 个 JSON 文件、177 个本地文档链接、13 个唯一评测用例；`git diff --check` 通过。仅清理 Program.cs 一处空行的尾随空格，未改变应用行为；未提交或推送。新包装层的 API 按本机包文档与 Microsoft Learn 核对，尚无新实现的编译或运行证据。
