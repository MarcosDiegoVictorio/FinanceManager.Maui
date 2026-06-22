using System;

namespace FinanceManager.Maui.Models;

public class TransactionDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string Category { get; set; } = "Geral";
    public bool IsFixed { get; set; }
    public int InstallmentNumber { get; set; } = 1;
    public int TotalInstallments { get; set; } = 1;
    public DateTime Date { get; set; }
    public bool IsIncome { get; set; }
    public int? GroupId { get; set; }
    public bool IsPrivate { get; set; }
}
