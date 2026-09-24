using System.IO;
using System.Text.Json;
using UIAutomationInspectorWpf.Models;

namespace UIAutomationInspectorWpf.Services;

public sealed class ProfileStore
{
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UIAutomationInspectorWpf",
        "automation-profile.json");

    public AutomationProfile Load()
    {
        if (!File.Exists(_filePath))
        {
            return CreateDefaultProfile();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var profile = JsonSerializer.Deserialize<AutomationProfile>(json) ?? CreateDefaultProfile();
            AddMissingElements(profile);
            return profile;
        }
        catch (JsonException)
        {
            return CreateDefaultProfile();
        }
        catch (IOException)
        {
            return CreateDefaultProfile();
        }
    }

    public void Save(AutomationProfile profile)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private static AutomationProfile CreateDefaultProfile()
    {
        var profile = new AutomationProfile
        {
            Elements = CreateElementDefinitions()
        };
        return profile;
    }

    private static List<AutomationElementDefinition> CreateElementDefinitions()
    {
        var names = new Dictionary<string, string>
        {
            ["OpenReceipt"] = "Открыть чек",
            ["ProductName"] = "Наименование",
            ["Price"] = "Цена",
            ["Quantity"] = "Количество",
            ["AddPosition"] = "Добавить позицию",
            ["ReceiptAttributes"] = "Атрибуты чека",
            ["CorrectionDate"] = "Дата коррекции",
            ["TransferCorrection"] = "Передать данные коррекции",
            ["OfdTags"] = "Теги ОФД",
            ["TagNumber"] = "Номер тега",
            ["TagDescription"] = "Описание тега",
            ["TagValue"] = "Значение тега",
            ["SendTag"] = "Отправить тег",
            ["FiscalOperations"] = "Операции ФН",
            ["Cash"] = "Наличные",
            ["Cashless"] = "Безналичные",
            ["Vat22"] = "НДС 22%",
            ["CloseReceipt"] = "Закрыть чек",
            ["Logs"] = "Логи внешней программы"
        };

        return names.Select(pair => new AutomationElementDefinition
        {
            Key = pair.Key,
            DisplayName = pair.Value
        }).ToList();
    }

    private static void AddMissingElements(AutomationProfile profile)
    {
        var existingKeys = profile.Elements.Select(element => element.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in CreateElementDefinitions())
        {
            if (!existingKeys.Contains(definition.Key))
            {
                profile.Elements.Add(definition);
            }
        }
    }
}