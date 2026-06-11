using SQLite;

namespace FinanceManager.Maui.Models;

[Table("BillsToPay")]
public class BillToPay
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_BillsToPay_UserId")]
    public int UserId { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Value { get; set; }

    public DateTime DueDate { get; set; }

    public int DaysBeforeToNotify { get; set; }
}
