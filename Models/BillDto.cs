using System;

namespace FinanceManager.Maui.Models;

public class BillDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public DateTime DueDate { get; set; }
    public int DaysBeforeToNotify { get; set; }
    public int TotalInstallments { get; set; } = 1;
}
