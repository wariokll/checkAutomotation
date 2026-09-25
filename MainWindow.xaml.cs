using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;
using UIAutomationInspectorWpf.Models;
using UIAutomationInspectorWpf.Services;

namespace UIAutomationInspectorWpf;

public partial class MainWindow : Window
{
    private readonly ProfileStore _profileStore = new();
    private readonly ExcelReceiptReader _excelReader = new();
    private readonly UiAutomationService _uiAutomation = new();
    private readonly ElementPickerService _picker;
    private readonly ReceiptAutomationRunner _runner;
    private readonly OfdApiExportService _ofdApiExportService = new();
    private readonly AutomationLogService _logService = new();
    private readonly ObservableCollection<WindowInfo> _windows = [];
    private AutomationProfile _profile;
    private AutomationRunLog? _currentRunLog;
    private string? _excelPath;
    private string? _pickingKey;
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _ofdCancellation;

    public MainWindow()
    {
        InitializeComponent();
        _profile = _profileStore.Load();
        _picker = new ElementPickerService();
        _picker.ElementPicked += ElementPicker_ElementPicked;
        _runner = new ReceiptAutomationRunner(_uiAutomation);
        TargetWindowComboBox.ItemsSource = _windows;
        ElementSettingsList.ItemsSource = _profile.Elements;
        OfdStartDatePicker.SelectedDate = DateTime.Today;
        OfdEndDatePicker.SelectedDate = DateTime.Today;
        RefreshWindows();
    }

    private void RefreshWindows_Click(object sender, RoutedEventArgs e) => RefreshWindows();

    private void RefreshWindows()
    {
        _windows.Clear();
        foreach (var process in Process.GetProcesses()
                     .Where(process => process.MainWindowHandle != IntPtr.Zero)
                     .Select(process => new WindowInfo(process.ProcessName, process.Id, process.MainWindowHandle, process.MainWindowTitle))
                     .Where(window => !string.IsNullOrWhiteSpace(window.Title))
                     .OrderBy(window => window.Title, StringComparer.OrdinalIgnoreCase))
        {
            _windows.Add(process);
        }

        StatusText.Text = $"Найдено окон: {_windows.Count}";
    }

    private void PickElement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key })
        {
            return;
        }

        _pickingKey = key;
        StatusText.Text = "Кликните нужный элемент в целевом окне...";
        _picker.Start();
    }

    private void ElementPicker_ElementPicked(object? sender, AutomationElement element)
    {
        if (_pickingKey is null)
        {
            return;
        }

        var definition = _profile.Elements.First(elementDefinition => elementDefinition.Key == _pickingKey);
        var updated = _uiAutomation.Describe(element, definition.Key, definition.DisplayName);
        definition.AutomationId = updated.AutomationId;
        definition.Name = updated.Name;
        definition.ClassName = updated.ClassName;
        definition.ControlType = updated.ControlType;
        definition.ProcessId = updated.ProcessId;
        definition.NativeWindowHandle = updated.NativeWindowHandle;
        if (definition.ProcessId != 0)
        {
            var targetWindow = _windows.FirstOrDefault(window => window.ProcessId == definition.ProcessId);
            if (targetWindow is not null)
            {
                TargetWindowComboBox.SelectedItem = targetWindow;
                _profile.TargetWindowTitle = targetWindow.Title;
                _profile.TargetProcessId = targetWindow.ProcessId;
            }
        }
        ElementSettingsList.Items.Refresh();
        try
        {
            _profileStore.Save(_profile);
            StatusText.Text = $"Сохранен элемент: {definition.DisplayName}";
        }
        catch (IOException exception)
        {
            StatusText.Text = $"Элемент выбран, но профиль не сохранен: {exception.Message}";
        }
        _pickingKey = null;
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (TargetWindowComboBox.SelectedItem is WindowInfo window)
        {
            _profile.TargetWindowTitle = window.Title;
            _profile.TargetProcessId = window.ProcessId;
        }

        try
        {
            _profileStore.Save(_profile);
            StatusText.Text = "Настройки сохранены";
        }
        catch (IOException exception)
        {
            ShowError($"Не удалось сохранить настройки: {exception.Message}");
        }
    }

    private void ResetProfile_Click(object sender, RoutedEventArgs e)
    {
        _profile = _profileStore.Load();
        ElementSettingsList.ItemsSource = _profile.Elements;
        StatusText.Text = "Профиль перезагружен";
    }

    private void SelectExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var rows = _excelReader.Read(dialog.FileName, AddLog);
            var groups = ExcelReceiptReader.GroupByFiscalDocument(rows);
            _excelPath = dialog.FileName;
            ExcelPathText.Text = dialog.FileName;
            PreviewTextBox.Text = BuildPreview(rows, groups);
            RunButton.IsEnabled = rows.Count > 0;
            StatusText.Text = $"Прочитано строк: {rows.Count}";
            AddLog($"Excel загружен: {rows.Count} строк, {groups.Count} чеков.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or FormatException)
        {
            AddLog($"Ошибка чтения Excel: {exception.Message}");
            ShowError($"Ошибка чтения Excel: {exception.Message}");
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_excelPath is null || TargetWindowComboBox.SelectedItem is not WindowInfo window)
        {
            ShowError("Выберите Excel-файл и окно назначения.");
            return;
        }

        try
        {
            _runCancellation = new CancellationTokenSource();
            _currentRunLog = _logService.StartRun();
            AddLog($"Начало запуска обработки. Excel: {_excelPath}");
            var rows = _excelReader.Read(_excelPath, AddLog);
            var allGroups = ExcelReceiptReader.GroupByFiscalDocument(rows);
            var (startIndex, endIndex) = ReadReceiptRange(allGroups.Count);
            var groups = allGroups.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
            RunButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            AddLog($"Запуск обработки чеков {startIndex + 1}-{endIndex + 1} из {allGroups.Count}. Файл лога: {_currentRunLog.FilePath}");
            var progress = new Progress<string>(AddLog);
            await _runner.RunAsync(_profile, window.Handle, groups, progress, _runCancellation.Token, startIndex);
            AddLog("Обработка завершена.");
            StatusText.Text = "Обработка завершена";
        }
        catch (OperationCanceledException)
        {
            AddLog("Обработка остановлена пользователем.");
            StatusText.Text = "Обработка остановлена";
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ElementNotAvailableException)
        {
            AddLog($"Ошибка UI Automation: {exception.Message}");
            ShowError($"Ошибка UI Automation: {exception.Message}");
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            _currentRunLog?.Dispose();
            _currentRunLog = null;
            RunButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _runCancellation?.Cancel();

    private async void ExportOfd_Click(object sender, RoutedEventArgs e)
    {
        if (OfdStartDatePicker.SelectedDate is not DateTime startDate ||
            OfdEndDatePicker.SelectedDate is not DateTime endDate)
        {
            ShowError("Укажите начальную и конечную даты для выгрузки ОФД.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"ofd-{startDate:yyyy-MM-dd}-{endDate:yyyy-MM-dd}.xlsx",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _ofdCancellation = new CancellationTokenSource();
        CancelOfdButton.IsEnabled = true;
        OfdOutputPathText.Text = dialog.FileName;
        OfdLogTextBox.Clear();
        AppendOfdLog($"Начало выгрузки ОФД: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}.");

        try
        {
            var progress = new Progress<string>(AppendOfdLog);
            var rows = await _ofdApiExportService.ExportAsync(
                OfdTokenPasswordBox.Password,
                FiscalDriveNumberTextBox.Text,
                startDate,
                endDate,
                dialog.FileName,
                progress,
                _ofdCancellation.Token);
            AppendOfdLog($"Выгрузка завершена. Получено строк: {rows}.");
        }
        catch (OperationCanceledException)
        {
            AppendOfdLog("Выгрузка ОФД остановлена пользователем.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException)
        {
            AppendOfdLog($"Ошибка выгрузки ОФД: {exception.Message}");
            ShowError($"Ошибка выгрузки ОФД: {exception.Message}");
        }
        finally
        {
            _ofdCancellation.Dispose();
            _ofdCancellation = null;
            CancelOfdButton.IsEnabled = false;
        }
    }

    private void CancelOfd_Click(object sender, RoutedEventArgs e) => _ofdCancellation?.Cancel();

    private void AppendOfdLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        OfdLogTextBox.AppendText(line + Environment.NewLine);
        OfdLogTextBox.ScrollToEnd();
        StatusText.Text = message;
        _logService.Write(message);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogTextBox.Clear();

    private (int StartIndex, int EndIndex) ReadReceiptRange(int totalCount)
    {
        if (!int.TryParse(StartReceiptTextBox.Text, out var start) || start < 1)
        {
            throw new InvalidOperationException("Номер первого чека должен быть положительным числом.");
        }

        var end = string.IsNullOrWhiteSpace(EndReceiptTextBox.Text)
            ? totalCount
            : int.TryParse(EndReceiptTextBox.Text, out var parsedEnd) ? parsedEnd : 0;
        if (end < start || end > totalCount)
        {
            throw new InvalidOperationException($"Диапазон чеков должен быть от 1 до {totalCount}, причем начальный номер не больше конечного.");
        }

        return (start - 1, end - 1);
    }

    private void AddLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        LogTextBox.AppendText(line + Environment.NewLine);
        LogTextBox.ScrollToEnd();
        StatusText.Text = message;
        if (_currentRunLog is null)
        {
            _logService.Write(message);
        }
        else
        {
            _currentRunLog.Write(message);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _picker.Dispose();
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _ofdCancellation?.Cancel();
        _ofdCancellation?.Dispose();
        base.OnClosed(e);
    }

    private static string BuildPreview(IReadOnlyList<ReceiptRow> rows, IReadOnlyList<ReceiptGroup> groups)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Строк: {rows.Count}");
        builder.AppendLine($"Чеков по ФД: {groups.Count}");
        builder.AppendLine();
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            builder.AppendLine($"Чек {index + 1} | ФД {group.FiscalDocumentNumber} | {group.FormationTime:dd.MM.yyyy HH:mm} | позиций: {group.Rows.Count}, сумма: {group.TotalPrice:0.##}, НДС: {group.TotalVat:0.##}, оплата: {group.PaymentMethod}");
        }

        return builder.ToString();
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public sealed record WindowInfo(string ProcessName, int ProcessId, IntPtr Handle, string Title)
    {
        public override string ToString() => $"{ProcessName} ({Title})";
    }
}
