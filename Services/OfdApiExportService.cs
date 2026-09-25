using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace UIAutomationInspectorWpf.Services;

public sealed class OfdApiExportService
{
    private const string DocumentsEndpoint = "https://api.ofd-ya.ru/ofdapi/v1/documents";
    private readonly HttpClient _httpClient = new();

    public async Task<int> ExportAsync(
        string token,
        string fiscalDriveNumber,
        DateTime startDate,
        DateTime endDate,
        string outputPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Введите токен ОФД.");
        }

        if (string.IsNullOrWhiteSpace(fiscalDriveNumber))
        {
            throw new InvalidOperationException("Введите номер фискального накопителя.");
        }

        if (endDate.Date < startDate.Date)
        {
            throw new InvalidOperationException("Конечная дата не может быть раньше начальной.");
        }

        var rows = new List<Dictionary<string, string>>();
        var totalDays = (endDate.Date - startDate.Date).Days + 1;
        for (var dayIndex = 0; dayIndex < totalDays; dayIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var date = startDate.Date.AddDays(dayIndex);
            progress?.Report($"ОФД: запрос {dayIndex + 1}/{totalDays}, дата {date:dd.MM.yyyy}");
            var json = await RequestDayAsync(token, fiscalDriveNumber, date, cancellationToken);
            var documents = ExtractDocuments(json);
            foreach (var document in documents)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["_Дата запроса"] = date.ToString("yyyy-MM-dd")
                };
                Flatten(document, string.Empty, row);
                rows.Add(row);
            }

            progress?.Report($"ОФД: дата {date:dd.MM.yyyy}, документов получено: {documents.Count}");
        }

        WriteWorkbook(outputPath, rows);
        progress?.Report($"ОФД: Excel сохранён, строк: {rows.Count}");
        return rows.Count;
    }

    private async Task<JsonDocument> RequestDayAsync(
        string token,
        string fiscalDriveNumber,
        DateTime date,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DocumentsEndpoint);
        request.Headers.TryAddWithoutValidation("Ofdapitoken", token.Trim());
        var body = new
        {
            fiscalDriveNumber,
            date = date.ToString("yyyy-MM-dd")
        };
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ОФД вернул HTTP {(int)response.StatusCode}: {TrimError(responseBody)}");
        }

        try
        {
            return JsonDocument.Parse(responseBody);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"ОФД вернул некорректный JSON: {exception.Message}");
        }
    }

    private static List<JsonElement> ExtractDocuments(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "documents", "data", "items", "result" })
            {
                if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
                {
                    return property.EnumerateArray().ToList();
                }
            }
        }

        return [root.Clone()];
    }

    private static void Flatten(JsonElement element, string prefix, IDictionary<string, string> values)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                Flatten(property.Value, name, values);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            values[prefix] = element.GetRawText();
            return;
        }

        values[prefix] = element.ValueKind == JsonValueKind.Null
            ? string.Empty
            : element.ToString();
    }

    private static void WriteWorkbook(string outputPath, IReadOnlyList<Dictionary<string, string>> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("ОФД");
        var columns = rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (columns.Count == 0)
        {
            columns.Add("Результат");
        }

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var cell = worksheet.Cell(1, columnIndex + 1);
            cell.Value = columns[columnIndex];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                rows[rowIndex].TryGetValue(columns[columnIndex], out var value);
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value = value ?? string.Empty;
            }
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(outputPath);
    }

    private static string TrimError(string responseBody)
    {
        var value = responseBody.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return value.Length > 500 ? value[..500] : value;
    }
}
