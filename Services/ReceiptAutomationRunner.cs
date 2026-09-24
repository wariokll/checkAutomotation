using System.Globalization;
using System.Windows.Automation;
using UIAutomationInspectorWpf.Models;

namespace UIAutomationInspectorWpf.Services;

public sealed class ReceiptAutomationRunner
{
    private static readonly TimeSpan UiActionDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LogRetryDelay = TimeSpan.FromSeconds(1);
    private const string NoErrorsLog = "(0) Ошибок нет";

    private readonly UiAutomationService _uiAutomation;

    public ReceiptAutomationRunner(UiAutomationService uiAutomation)
    {
        _uiAutomation = uiAutomation;
    }

    public async Task RunAsync(
        AutomationProfile profile,
        IntPtr windowHandle,
        IReadOnlyList<ReceiptGroup> groups,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        _uiAutomation.ActivateWindow(windowHandle);
        await Task.Delay(200, cancellationToken);
        var root = _uiAutomation.FindRoot(windowHandle)
            ?? throw new InvalidOperationException("Не удалось получить окно назначения.");

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Чек ФД {group.FiscalDocumentNumber}: открытие");
            await OpenReceiptUntilLogIsClearAsync(root, profile, progress, cancellationToken);

            foreach (var row in group.Rows)
            {
                SetValue(root, profile, "ProductName", row.ProductName);
                SetValue(root, profile, "Price", row.ItemPrice.ToString("0.##", CultureInfo.InvariantCulture));
                SetValue(root, profile, "Quantity", row.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                Invoke(root, profile, "AddPosition");
                await DelayAfterUiActionAsync(cancellationToken);
            }

            Invoke(root, profile, "ReceiptAttributes");
            SetDateValue(root, profile, "CorrectionDate", group.FormationTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            Invoke(root, profile, "TransferCorrection");
            await DelayAfterUiActionAsync(cancellationToken);
            Invoke(root, profile, "OfdTags");
            SetValue(root, profile, "TagNumber", "1192");
            Invoke(root, profile, "TagDescription");
            SetValue(root, profile, "TagValue", group.FiscalSign);
            Invoke(root, profile, "SendTag");
            await DelayAfterUiActionAsync(cancellationToken);
            Invoke(root, profile, "FiscalOperations");

            if (group.PaymentMethod.Contains("налич", StringComparison.OrdinalIgnoreCase))
            {
                SetValue(root, profile, "Cash", group.TotalPrice.ToString("0.##", CultureInfo.InvariantCulture));
            }
            else
            {
                SetValue(root, profile, "Cashless", group.TotalPrice.ToString("0.##", CultureInfo.InvariantCulture));
            }

            SetValue(root, profile, "Vat22", group.TotalVat.ToString("0.##", CultureInfo.InvariantCulture));
            Invoke(root, profile, "CloseReceipt");
            await DelayAfterUiActionAsync(cancellationToken);
            var closeLog = ReadText(root, profile, "Logs");
            progress?.Report($"Чек ФД {group.FiscalDocumentNumber}: лог после закрытия: {closeLog}");
            progress?.Report($"Чек ФД {group.FiscalDocumentNumber}: завершен");
        }
    }

    private async Task OpenReceiptUntilLogIsClearAsync(
        AutomationElement root,
        AutomationProfile profile,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invoke(root, profile, "OpenReceipt");
            await DelayAfterUiActionAsync(cancellationToken);
            var log = ReadText(root, profile, "Logs").Trim();
            if (string.Equals(log, NoErrorsLog, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            progress?.Report($"Лог открытия содержит ошибку: {log}. Повтор через 1 секунду.");
            await Task.Delay(LogRetryDelay, cancellationToken);
        }
    }

    private async Task DelayAfterUiActionAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(UiActionDelay, cancellationToken);
    }

    private void Invoke(AutomationElement root, AutomationProfile profile, string key)
    {
        _uiAutomation.Invoke(ResolveRequired(root, profile, key));
    }

    private void SetValue(AutomationElement root, AutomationProfile profile, string key, string value)
    {
        _uiAutomation.SetValue(ResolveRequired(root, profile, key), value);
    }

    private void SetDateValue(AutomationElement root, AutomationProfile profile, string key, string value)
    {
        _uiAutomation.SetDateValue(ResolveRequired(root, profile, key), value);
    }

    private string ReadText(AutomationElement root, AutomationProfile profile, string key)
    {
        return _uiAutomation.ReadText(ResolveRequired(root, profile, key));
    }

    private AutomationElement ResolveRequired(AutomationElement root, AutomationProfile profile, string key)
    {
        var definition = profile.Elements.FirstOrDefault(element => element.Key == key)
            ?? throw new InvalidOperationException($"В настройках не сохранен элемент «{key}».");
        return _uiAutomation.Resolve(root, definition)
            ?? throw new InvalidOperationException($"Не найден элемент «{definition.DisplayName}».");
    }
}