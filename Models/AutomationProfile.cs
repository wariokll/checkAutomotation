namespace UIAutomationInspectorWpf.Models;

public sealed class AutomationProfile
{
    public string TargetWindowTitle { get; set; } = string.Empty;
    public int TargetProcessId { get; set; }
    public List<AutomationElementDefinition> Elements { get; set; } = [];
}