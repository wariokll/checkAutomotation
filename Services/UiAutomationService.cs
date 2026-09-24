using System.Runtime.InteropServices;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using UIAutomationInspectorWpf.Models;

namespace UIAutomationInspectorWpf.Services;

public sealed class UiAutomationService
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public AutomationElement? FindRoot(IntPtr windowHandle)
    {
        return windowHandle == IntPtr.Zero ? null : AutomationElement.FromHandle(windowHandle);
    }

    public void ActivateWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(windowHandle, 9);
        SetForegroundWindow(windowHandle);
    }

    public AutomationElementDefinition Describe(AutomationElement element, string key, string displayName)
    {
        return new AutomationElementDefinition
        {
            Key = key,
            DisplayName = displayName,
            AutomationId = element.Current.AutomationId,
            Name = element.Current.Name,
            ClassName = element.Current.ClassName,
            ControlType = element.Current.ControlType?.ProgrammaticName ?? string.Empty,
            ProcessId = element.Current.ProcessId,
            NativeWindowHandle = element.Current.NativeWindowHandle
        };
    }

    public AutomationElement? Resolve(AutomationElement root, AutomationElementDefinition definition)
    {
        var condition = BuildCondition(definition);
        if (condition is null)
        {
            return null;
        }

        var owningRoot = definition.NativeWindowHandle != 0
            ? AutomationElement.FromHandle(new IntPtr(definition.NativeWindowHandle))
            : root;
        var element = owningRoot?.FindFirst(TreeScope.Descendants, condition);
        if (element is not null)
        {
            return element;
        }

        element = FindInRawView(owningRoot, definition);
        if (element is not null)
        {
            return element;
        }

        element = FindInAutomationTree(definition, condition);
        if (element is not null)
        {
            return element;
        }

        return FindInRawView(AutomationElement.RootElement, definition);
    }

    public void Invoke(AutomationElement element)
    {
        ActivateElement(element);
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) &&
            pattern is InvokePattern invoke)
        {
            invoke.Invoke();
            return;
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPattern) &&
            selectionPattern is SelectionItemPattern selectionItem)
        {
            selectionItem.Select();
            return;
        }

        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPattern) &&
            expandPattern is ExpandCollapsePattern expandCollapse)
        {
            expandCollapse.Expand();
            return;
        }

        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern) &&
            togglePattern is TogglePattern toggle)
        {
            toggle.Toggle();
            return;
        }

        throw new InvalidOperationException($"Элемент «{element.Current.Name}» не поддерживает доступный способ активации.");
    }

    public void SetValue(AutomationElement element, string value)
    {
        ActivateElement(element);
        if (TrySetValue(element, value))
        {
            return;
        }

        throw new InvalidOperationException($"Поле «{element.Current.Name}» не поддерживает ValuePattern.");
    }

    public void SetDateValue(AutomationElement element, string value)
    {
        ActivateElement(element);
        if (!DateTime.TryParseExact(value, "dd.MM.yyyy", RussianCulture, DateTimeStyles.None, out var targetDate) &&
            !DateTime.TryParse(value, RussianCulture, DateTimeStyles.AllowWhiteSpaces, out targetDate))
        {
            throw new FormatException($"Некорректная дата DatePicker: «{value}».");
        }

        if (TrySetDateParts(element, targetDate))
        {
            return;
        }

        if (TrySetDateWithSegmentKeyboard(element, targetDate))
        {
            return;
        }

        throw new InvalidOperationException($"Не удалось ввести дату «{value}» через сегменты DatePicker.");
    }

    public string ReadText(AutomationElement element)
    {
        ActivateElement(element);
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) &&
            valuePatternObject is ValuePattern valuePattern)
        {
            return valuePattern.Current.Value;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
            textPatternObject is TextPattern textPattern)
        {
            return textPattern.DocumentRange.GetText(-1);
        }

        return element.Current.Name;
    }

    private static void ActivateElement(AutomationElement element)
    {
        try
        {
            var windowHandle = element.Current.NativeWindowHandle;
            if (windowHandle != 0)
            {
                ShowWindow(new IntPtr(windowHandle), 9);
                SetForegroundWindow(new IntPtr(windowHandle));
            }

            element.SetFocus();
        }
        catch (ElementNotAvailableException)
        {
            // The automation provider can still expose a usable pattern without focus.
        }
        catch (InvalidOperationException)
        {
            // Some providers do not support focus for every element.
        }
    }

    private static bool TrySetValue(AutomationElement element, string value)
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ||
            pattern is not ValuePattern valuePattern ||
            valuePattern.Current.IsReadOnly)
        {
            return false;
        }

        valuePattern.SetValue(value);
        return true;
    }

    private static bool TrySetDateParts(AutomationElement datePicker, DateTime value)
    {
        var descendants = datePicker.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .ToList();
        var day = FindDatePart(descendants, "день", "day");
        var month = FindDatePart(descendants, "месяц", "month");
        var year = FindDatePart(descendants, "год", "year");

        if (day is null || month is null || year is null)
        {
            var editParts = descendants
                .Where(element => element.Current.ControlType == ControlType.Edit)
                .OrderBy(element => element.Current.BoundingRectangle.Left)
                .ToList();
            if (editParts.Count < 3)
            {
                return false;
            }

            day = editParts[0];
            month = editParts[1];
            year = editParts[2];
        }

        return TrySetDatePart(day, value.Day.ToString(CultureInfo.InvariantCulture)) &&
               TrySetDatePart(month, value.Month.ToString(CultureInfo.InvariantCulture)) &&
               TrySetDatePart(year, value.Year.ToString(CultureInfo.InvariantCulture));
    }

    private static AutomationElement? FindDatePart(
        IReadOnlyList<AutomationElement> elements,
        string russianName,
        string englishName)
    {
        return elements.FirstOrDefault(element =>
        {
            var name = element.Current.Name.Trim();
            return name.Contains(russianName, StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(englishName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool TrySetDatePart(AutomationElement element, string value)
    {
        if (TrySetValue(element, value) || TrySetNumericValue(element, value))
        {
            return true;
        }

        foreach (AutomationElement child in element.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (TrySetValue(child, value) || TrySetNumericValue(child, value))
            {
                return true;
            }
        }

        return TrySetWithKeyboard(element, value);
    }

    private static bool TrySetWithKeyboard(AutomationElement element, string value)
    {
        try
        {
            ActivateElement(element);
            KeybdEvent(0x11, 0, 0, UIntPtr.Zero);
            KeybdEvent(0x41, 0, 0, UIntPtr.Zero);
            KeybdEvent(0x41, 0, KeyEventKeyUp, UIntPtr.Zero);
            KeybdEvent(0x11, 0, KeyEventKeyUp, UIntPtr.Zero);

            foreach (var character in value)
            {
                KeybdEvent(0, (byte)character, KeyEventUnicode, UIntPtr.Zero);
                KeybdEvent(0, (byte)character, KeyEventUnicode | KeyEventKeyUp, UIntPtr.Zero);
            }

            KeybdEvent(0x09, 0, 0, UIntPtr.Zero);
            KeybdEvent(0x09, 0, KeyEventKeyUp, UIntPtr.Zero);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TrySetDateWithSegmentKeyboard(AutomationElement element, DateTime value)
    {
        try
        {
            var yearSegment = FindYearSegment(element);
            if (yearSegment is not null)
            {
                ActivateElement(yearSegment);
            }
            else
            {
                ActivateElement(element);
                FocusYearSegment(element);
            }

            SendUnicodeText(value.Year.ToString(CultureInfo.InvariantCulture));
            SendVirtualKey(0x25);
            SendUnicodeText(value.Month.ToString("00", CultureInfo.InvariantCulture));
            SendVirtualKey(0x25);
            SendUnicodeText(value.Day.ToString("00", CultureInfo.InvariantCulture));
            SendVirtualKey(0x09);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static void SendUnicodeText(string value)
    {
        foreach (var character in value)
        {
            KeybdEvent(0, (byte)character, KeyEventUnicode, UIntPtr.Zero);
            KeybdEvent(0, (byte)character, KeyEventUnicode | KeyEventKeyUp, UIntPtr.Zero);
        }
    }

    private static void SendVirtualKey(byte virtualKey)
    {
        KeybdEvent(virtualKey, 0, 0, UIntPtr.Zero);
        KeybdEvent(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void FocusYearSegment(AutomationElement element)
    {
        var rectangle = element.Current.BoundingRectangle;
        if (rectangle.IsEmpty || rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            ActivateElement(element);
            return;
        }

        var yearX = rectangle.Left + rectangle.Width * 0.72;
        var yearY = rectangle.Top + rectangle.Height / 2;
        SetCursorPos((int)yearX, (int)yearY);
        MouseEvent(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
        MouseEvent(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
    }

    private static AutomationElement? FindYearSegment(AutomationElement datePicker)
    {
        var elements = datePicker.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(element => element.Current.ControlType == ControlType.Edit)
            .ToList();

        foreach (var element in elements)
        {
            var text = element.Current.Name;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                pattern is ValuePattern valuePattern)
            {
                text = valuePattern.Current.Value;
            }

            var match = Regex.Match(text ?? string.Empty, @"(?<!\d)(\d{4})(?!\d)");
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var year) &&
                year >= 1900 && year <= 2200)
            {
                return element;
            }
        }

        return null;
    }

    private static bool TrySetNumericValue(AutomationElement element, string value)
    {
        if (!double.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ||
            !element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern) ||
            pattern is not RangeValuePattern rangeValue ||
            rangeValue.Current.IsReadOnly ||
            number < rangeValue.Current.Minimum ||
            number > rangeValue.Current.Maximum)
        {
            return false;
        }

        rangeValue.SetValue(number);
        return true;
    }

    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    [DllImport("user32.dll", EntryPoint = "keybd_event", SetLastError = true)]
    private static extern void KeybdEvent(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event", SetLastError = true)]
    private static extern void MouseEvent(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        UIntPtr extraInfo);

    private static void ExpandDatePicker(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPattern) &&
            expandPattern is ExpandCollapsePattern expandCollapse)
        {
            expandCollapse.Expand();
            return;
        }

        foreach (AutomationElement child in element.FindAll(
                     TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        {
            if (child.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern) &&
                invokePattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return;
            }
        }
    }

    private static Condition? BuildCondition(AutomationElementDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.AutomationId))
        {
            return new PropertyCondition(AutomationElement.AutomationIdProperty, definition.AutomationId);
        }

        if (!string.IsNullOrWhiteSpace(definition.Name))
        {
            return new PropertyCondition(AutomationElement.NameProperty, definition.Name);
        }

        if (!string.IsNullOrWhiteSpace(definition.ClassName))
        {
            return new PropertyCondition(AutomationElement.ClassNameProperty, definition.ClassName);
        }

        return null;
    }

    private static AutomationElement? FindInRawView(
        AutomationElement? root,
        AutomationElementDefinition definition)
    {
        if (root is null)
        {
            return null;
        }

        var inspected = 0;
        return FindRecursively(root, definition, ref inspected);
    }

    private static AutomationElement? FindRecursively(
        AutomationElement element,
        AutomationElementDefinition definition,
        ref int inspected)
    {
        if (inspected++ >= 20000)
        {
            return null;
        }

        try
        {
            var current = element.Current;
            if ((definition.ProcessId == 0 || current.ProcessId == definition.ProcessId) &&
                Matches(current, definition))
            {
                return element;
            }

            var child = TreeWalker.RawViewWalker.GetFirstChild(element);
            while (child is not null)
            {
                var match = FindRecursively(child, definition, ref inspected);
                if (match is not null)
                {
                    return match;
                }

                child = TreeWalker.RawViewWalker.GetNextSibling(child);
            }
        }
        catch (ElementNotAvailableException)
        {
            // The target can change while the raw tree is traversed.
        }

        return null;
    }

    private static AutomationElement? FindInAutomationTree(
        AutomationElementDefinition definition,
        Condition condition)
    {
        try
        {
            var elements = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition);
            foreach (AutomationElement element in elements)
            {
                if (definition.ProcessId == 0 || element.Current.ProcessId == definition.ProcessId)
                {
                    return element;
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            // The target can change while the global tree is queried.
        }

        return null;
    }

    private static bool Matches(AutomationElement.AutomationElementInformation current, AutomationElementDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.AutomationId) &&
            string.Equals(current.AutomationId, definition.AutomationId, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(definition.AutomationId) &&
            !string.IsNullOrWhiteSpace(definition.Name) &&
            string.Equals(current.Name, definition.Name, StringComparison.Ordinal))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(definition.AutomationId) &&
               string.IsNullOrWhiteSpace(definition.Name) &&
               !string.IsNullOrWhiteSpace(definition.ClassName) &&
               string.Equals(current.ClassName, definition.ClassName, StringComparison.Ordinal);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);
}