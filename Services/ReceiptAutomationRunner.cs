using System.Globalization;
using System.Windows.Automation;
using UIAutomationInspectorWpf.Models;

namespace UIAutomationInspectorWpf.Services;

public sealed class ReceiptAutomationRunner
{
    private static readonly TimeSpan UiActionDelay = TimeSpan.FromMilliseconds(250);
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
        CancellationToken cancellationToken,
        int receiptStartIndex = 0)
    {
        _uiAutomation.ActivateWindow(windowHandle);
        await Task.Delay(200, cancellationToken);
        var root = _uiAutomation.FindRoot(windowHandle)
            ?? throw new InvalidOperationException("Не удалось получить окно назначения.");

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var receiptNumber = receiptStartIndex + groupIndex + 1;
            var receiptRange = $"Чек {receiptNumber}/{receiptStartIndex + groups.Count}";
            progress?.Report($"{receiptRange}, ФД {group.FiscalDocumentNumber}: открытие");
            await OpenReceiptUntilLogIsClearAsync(root, profile, progress, cancellationToken);

            foreach (var row in group.Rows)
            {
                SetValue(root, profile, "ProductName", row.ProductName, progress, "Ввод наименования товара");
                SetValue(root, profile, "Price", row.ItemPrice.ToString("0.##", CultureInfo.InvariantCulture), progress, "Ввод цены");
                SetValue(root, profile, "Quantity", row.Quantity.ToString("0.##", CultureInfo.InvariantCulture), progress, "Ввод количества");
                Invoke(root, profile, "AddPosition", progress, "Нажатие: добавить позицию");
                await DelayAfterUiActionAsync(cancellationToken);
            }

            Invoke(root, profile, "ReceiptAttributes", progress, "Нажатие: атрибуты чека");
            SetDateValue(root, profile, "CorrectionDate", group.FormationTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), progress, "Ввод даты коррекции");
            Invoke(root, profile, "TransferCorrection", progress, "Нажатие: передать данные коррекции");
            await DelayAfterUiActionAsync(cancellationToken);
            Invoke(root, profile, "OfdTags", progress, "Нажатие: теги ОФД");
            SetValue(root, profile, "TagNumber", "1192", progress, "Ввод номера тега 1192");
            Invoke(root, profile, "TagDescription", progress, "Нажатие: описание тега");
            SetValue(root, profile, "TagValue", group.FiscalSign, progress, "Ввод значения ФПД");
            Invoke(root, profile, "SendTag", progress, "Нажатие: отправить тег");
            await DelayAfterUiActionAsync(cancellationToken);
            Invoke(root, profile, "FiscalOperations", progress, "Нажатие: операции ФН");
            SetValue(root, profile, "Cash", "0", progress, "Обнуление поля наличных");
            SetValue(root, profile, "Cashless", "0", progress, "Обнуление поля безналичных");

            if (IsCashPayment(group.PaymentMethod))
            {
                progress?.Report($"Способ оплаты «{group.PaymentMethod}»: поле наличных");
                SetValue(root, profile, "Cash", group.TotalPrice.ToString("0.##", CultureInfo.InvariantCulture), progress, "Ввод суммы наличных");
            }
            else if (IsCashlessPayment(group.PaymentMethod))
            {
                progress?.Report($"Способ оплаты «{group.PaymentMethod}»: поле безналичных");
                SetValue(root, profile, "Cashless", group.TotalPrice.ToString("0.##", CultureInfo.InvariantCulture), progress, "Ввод суммы безналичных");
            }
            else
            {
                throw new InvalidOperationException($"Неизвестный способ оплаты в Excel: «{group.PaymentMethod}».");
            }

            SetValue(root, profile, "Vat22", group.TotalVat.ToString("0.##", CultureInfo.InvariantCulture), progress, "Ввод НДС 22%");
            Invoke(root, profile, "CloseReceipt", progress, "Нажатие: закрыть чек");
            await DelayAfterUiActionAsync(cancellationToken);
            var closeLog = ReadText(root, profile, "Logs", progress, "Чтение внешнего лога после закрытия");
            progress?.Report($"{receiptRange}, ФД {group.FiscalDocumentNumber}: лог после закрытия: {closeLog}");
            progress?.Report($"{receiptRange}, ФД {group.FiscalDocumentNumber}: завершен");
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
            Invoke(root, profile, "OpenReceipt", progress, "Нажатие: открыть чек");
            await DelayAfterUiActionAsync(cancellationToken);
            var log = ReadText(root, profile, "Logs", progress, "Чтение внешнего лога после открытия").Trim();
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

    private void Invoke(AutomationElement root, AutomationProfile profile, string key, IProgress<string>? progress, string operation)
    {
        progress?.Report(operation);
        _uiAutomation.Invoke(ResolveRequired(root, profile, key));
    }

    private void SetValue(AutomationElement root, AutomationProfile profile, string key, string value, IProgress<string>? progress, string operation)
    {
        progress?.Report($"{operation}: {value}");
        _uiAutomation.SetValue(ResolveRequired(root, profile, key), value);
    }

    private void SetDateValue(AutomationElement root, AutomationProfile profile, string key, string value, IProgress<string>? progress, string operation)
    {
        progress?.Report($"{operation}: {value}");
        _uiAutomation.SetDateValue(ResolveRequired(root, profile, key), value);
    }

    private string ReadText(AutomationElement root, AutomationProfile profile, string key, IProgress<string>? progress, string operation)
    {
        progress?.Report(operation);
        return _uiAutomation.ReadText(ResolveRequired(root, profile, key));
    }

    private AutomationElement ResolveRequired(AutomationElement root, AutomationProfile profile, string key)
    {
        var definition = profile.Elements.FirstOrDefault(element => element.Key == key)
            ?? throw new InvalidOperationException($"В настройках не сохранен элемент «{key}».");
        return _uiAutomation.Resolve(root, definition)
            ?? throw new InvalidOperationException($"Не найден элемент «{definition.DisplayName}».");
    }

    private static bool IsCashPayment(string paymentMethod)
    {
        return !IsCashlessPayment(paymentMethod) &&
               paymentMethod.Contains("налич", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCashlessPayment(string paymentMethod)
    {
        return paymentMethod.Contains("безнал", StringComparison.OrdinalIgnoreCase) ||
               paymentMethod.Contains("карта", StringComparison.OrdinalIgnoreCase);
    }
}