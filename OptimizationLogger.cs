namespace HellDrive;

public sealed class OptimizationLogger
{
    private readonly string _file = Path.Combine(AppContext.BaseDirectory, "optimization.log");
    public List<OptimizationAction> Entries { get; } = new();

    public void Add(string action, string detail, bool reversible = true)
    {
        var item = new OptimizationAction(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), action, detail, reversible);
        Entries.Insert(0, item);
        try { File.AppendAllText(_file, $"[{item.Time}] {item.Action} | {item.Detail}\r\n"); } catch { }
    }
}
