using System.Globalization;
using ClosedXML.Excel;
using UIAutomationInspectorWpf.Models;

namespace UIAutomationInspectorWpf.Services;

public sealed class ExcelReceiptReader
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public IReadOnlyList<ReceiptRow> Read(string filePath, Action<string>? log = null)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("В Excel-файле не найден лист.");

        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException("Excel-файл не содержит строк.");

        var columns = headerRow.CellsUsed()
            .ToDictionary(cell => Normalize(cell.GetString()), cell => cell.Address.ColumnNumber);

        var required = new Dictionary<string, string>
        {
            ["Время формирования чека"] = "Время формирования чека",
            ["Наименование товара"] = "Наименование товара",
            ["Количество"] = "Количество",
            ["Цена"] = "Цена",
            ["Сумма товара"] = "Сумма товара",
            ["Способ оплаты"] = "Способ оплаты",
            ["Номер ФД"] = "Номер ФД",
            ["ФПД"] = "ФПД",
            ["НДС 22%"] = "НДС 22%"
        };

        var indexes = required.ToDictionary(
            pair => pair.Key,
            pair => FindColumn(columns, pair.Value));

        var result = new List<ReceiptRow>();
        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            if (row.CellsUsed().All(cell => string.IsNullOrWhiteSpace(cell.GetString())))
            {
                continue;
            }

            var formationCell = row.Cell(indexes["Время формирования чека"]);
            var productName = row.Cell(indexes["Наименование товара"]).GetString().Trim();
            var fiscalDocumentNumber = row.Cell(indexes["Номер ФД"]).GetString().Trim();
            if (IsEmptyOrMarker(formationCell.GetString()))
            {
                log?.Invoke($"Пропущена строка Excel {row.RowNumber()}: отсутствует дата. Товар: «{productName}», ФД: «{fiscalDocumentNumber}».");
                continue;
            }

            result.Add(new ReceiptRow
            {
                FormationTime = ReadDate(formationCell, row.RowNumber()),
                ProductName = productName,
                Quantity = ReadDecimal(row.Cell(indexes["Количество"])),
                ItemPrice = ReadDecimal(row.Cell(indexes["Цена"])),
                ItemAmount = ReadDecimal(row.Cell(indexes["Сумма товара"])),
                PaymentMethod = row.Cell(indexes["Способ оплаты"]).GetString().Trim(),
                FiscalDocumentNumber = fiscalDocumentNumber,
                FiscalSign = row.Cell(indexes["ФПД"]).GetString().Trim(),
                VatAmount = ReadDecimal(row.Cell(indexes["НДС 22%"])),
            });
        }

        return result;
    }

    public static IReadOnlyList<ReceiptGroup> GroupByFiscalDocument(IReadOnlyList<ReceiptRow> rows)
    {
        var groups = new List<ReceiptGroup>();
        foreach (var row in rows)
        {
            if (groups.Count == 0 || groups[^1].FiscalDocumentNumber != row.FiscalDocumentNumber)
            {
                groups.Add(new ReceiptGroup([row]));
                continue;
            }

            var currentRows = groups[^1].Rows.ToList();
            currentRows.Add(row);
            groups[^1] = new ReceiptGroup(currentRows);
        }

        return groups;
    }

    private static int FindColumn(IReadOnlyDictionary<string, int> columns, string header)
    {
        var key = Normalize(header);
        if (columns.TryGetValue(key, out var index))
        {
            return index;
        }

        throw new InvalidOperationException($"В Excel-файле отсутствует столбец «{header}».");
    }

    private static DateTime ReadDate(IXLCell cell, int rowNumber)
    {
        if (cell.TryGetValue<DateTime>(out var date))
        {
            return date;
        }

        if (DateTime.TryParse(cell.GetString(), RussianCulture, DateTimeStyles.None, out date))
        {
            return date;
        }

        throw new FormatException($"Не удалось прочитать дату в строке Excel {rowNumber}: «{cell.GetString()}».");
    }

    private static bool IsEmptyOrMarker(string value)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized == "<>";
    }

    private static decimal ReadDecimal(IXLCell cell)
    {
        if (cell.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        var text = cell.GetString().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (decimal.TryParse(text, NumberStyles.Number, RussianCulture, out number) ||
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new FormatException($"Не удалось прочитать число: «{cell.GetString()}».");
    }

    private static string Normalize(string value)
    {
        return value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("ё", "е", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToUpperInvariant();
    }
}