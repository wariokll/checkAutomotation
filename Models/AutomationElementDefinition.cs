namespace UIAutomationInspectorWpf.Models;

public sealed class AutomationElementDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AutomationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string ControlType { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public long NativeWindowHandle { get; set; }

    public override string ToString()
    {
        var selector = !string.IsNullOrWhiteSpace(AutomationId)
            ? $"AutomationId: {AutomationId}"
            : $"Name: {Name}";
        return $"{DisplayName} ({selector})";
    }
}