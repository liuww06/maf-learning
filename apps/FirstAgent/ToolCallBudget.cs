public class ToolCallBudget
{
    private readonly object _lock = new object();
    public ToolCallBudget(int limit)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "工具调用预算必须至少为 1。");
        }
        Limit = limit;
    }
    public int Limit { get; }

    // 诊断用的近似计数，允许过时；是否准许执行只依据 TryConsume 的返回值。
    public int Used { get; private set; }
    public bool TryConsume()
    {
        lock (_lock)
        {
            if (Used >= Limit)
            {
                Console.Error.WriteLine($"[tool-rejected] 超过工具调用预算 {Limit}，已使用 {Used}。");
                return false;
            }
            Used++;
            Console.Error.WriteLine($"[tool-admitted] 工具调用预算 {Limit}，已使用 {Used}。");
            return true;
        }
    }
}
