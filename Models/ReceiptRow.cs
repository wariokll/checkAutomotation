namespace UIAutomationInspectorWpf.Models;

public sealed class ReceiptRow
{
    public DateTime FormationTime { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal ItemPrice { get; init; }
    public decimal ItemAmount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string FiscalDocumentNumber { get; init; } = string.Empty;
    public string FiscalSign { get; init; } = string.Empty;
    public decimal VatAmount { get; init; }
}

public sealed class ReceiptGroup
{
    public ReceiptGroup(IReadOnlyList<ReceiptRow> rows)
    {
        Rows = rows;
        FiscalDocumentNumber = rows[0].FiscalDocumentNumber;
        FormationTime = rows[0].FormationTime;
        FiscalSign = rows[^1].FiscalSign;
        PaymentMethod = rows[^1].PaymentMethod;
        TotalPrice = rows.Sum(row => row.ItemAmount);
        TotalVat = rows.Sum(row => row.VatAmount);
    }

    public IReadOnlyList<ReceiptRow> Rows { get; }
    public string FiscalDocumentNumber { get; }
    public DateTime FormationTime { get; }
    public string FiscalSign { get; }
    public string PaymentMethod { get; }
    public decimal TotalPrice { get; }
    public decimal TotalVat { get; }
}