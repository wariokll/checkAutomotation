namespace UIAutomationInspectorWpf.Services;

public static class BuildExpirationService
{
    // Change this date for a new two-month build.
    private static readonly DateTime BuildDate = new(2026, 09, 24);
    private static readonly DateTime ExpirationDate = BuildDate.AddMonths(2);

    public static bool IsBuildValid()
    {
        if (DateTime.Today <= ExpirationDate.Date)
        {
            return true;
        }

        return false;
    }
}