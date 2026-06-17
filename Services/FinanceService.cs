using FinanceManager.Maui.Models;
using SQLite;
#if ANDROID || IOS
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
#endif
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.IO;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace FinanceManager.Maui.Services;

public class FinanceService
{
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly string _dbPath;

    public FinanceService(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task InitializeAsync()
    {
        if (_db is not null) return;

        await _initLock.WaitAsync();
        try
        {
            if (_db is not null) return;

            _db = new SQLiteAsyncConnection(_dbPath);
            await _db.CreateTableAsync<User>();
            await _db.CreateTableAsync<Transaction>();
            await _db.CreateTableAsync<Category>();
            await _db.CreateTableAsync<Group>();
            await _db.CreateTableAsync<GroupMember>();
            await _db.CreateTableAsync<BillToPay>();
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_Transactions_Year_Month ON Transactions (Year, Month)");
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_Transactions_UserId ON Transactions (UserId)");
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_Transactions_GroupId ON Transactions (GroupId)");
            await _db.ExecuteAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users (Username)");
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_Categories_UserId ON Categories (UserId)");
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_GroupMembers_GroupId ON GroupMembers (GroupId)");
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_GroupMembers_UserId ON GroupMembers (UserId)");
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS IX_BillsToPay_UserId ON BillsToPay (UserId)");

            await CleanRetroactiveReplicasAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task CleanRetroactiveReplicasAsync()
    {
        var allFixed = await _db!.Table<Transaction>()
            .Where(t => t.IsFixed && t.TotalInstallments <= 1)
            .ToListAsync();

        var toDelete = new List<Transaction>();

        foreach (var group in allFixed.GroupBy(t => new { t.UserId, t.Description, t.Category, t.Value, t.IsIncome }))
        {
            var original = group.OrderBy(t => t.Id).FirstOrDefault();
            if (original is null) continue;

            var originalMonthStart = new DateTime(original.Year, original.Month, 1);

            foreach (var item in group)
            {
                var itemMonthStart = new DateTime(item.Year, item.Month, 1);
                if (item.Id > original.Id && itemMonthStart < originalMonthStart)
                {
                    toDelete.Add(item);
                }
            }
        }

        foreach (var item in toDelete)
        {
            await _db.DeleteAsync(item);
        }
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        await InitializeAsync();
        return await _db!.Table<User>()
            .Where(u => u.Username.ToLower() == username.ToLower())
            .FirstOrDefaultAsync();
    }

    public async Task SaveUserAsync(User user)
    {
        await InitializeAsync();
        await _db!.InsertAsync(user);
    }

    public async Task<IReadOnlyList<Transaction>> GetAllAsync(int userId)
    {
        await InitializeAsync();
        return await _db!.Table<Transaction>()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Transaction>> GetTransactionsByMonth(int userId, int year, int month)
    {
        await InitializeAsync();
        await ReplicateFixedExpensesAsync(userId, year, month);

        var member = await _db!.Table<GroupMember>()
            .Where(gm => gm.UserId == userId)
            .FirstOrDefaultAsync();

        List<Transaction> items;
        if (member is not null)
        {
            items = await _db.Table<Transaction>()
                .Where(t => (t.UserId == userId || (t.GroupId == member.GroupId && !t.IsPrivate)) 
                            && t.Year == year && t.Month == month)
                .OrderByDescending(t => t.Date)
                .ToListAsync();
        }
        else
        {
            items = await _db.Table<Transaction>()
                .Where(t => t.UserId == userId && t.Year == year && t.Month == month)
                .OrderByDescending(t => t.Date)
                .ToListAsync();
        }

        var uniqueUserIds = items.Select(t => t.UserId).Distinct().ToList();
        var usersMap = new Dictionary<int, string>();
        foreach (var id in uniqueUserIds)
        {
            var u = await _db.Table<User>().Where(usr => usr.Id == id).FirstOrDefaultAsync();
            if (u is not null)
            {
                usersMap[id] = u.Name;
            }
        }

        foreach (var t in items)
        {
            t.CreatorName = usersMap.TryGetValue(t.UserId, out var name) ? name : "Desconhecido";
        }

        return items;
    }

    public Task<IReadOnlyList<Transaction>> GetByMonthAsync(int userId, int year, int month) =>
        GetTransactionsByMonth(userId, year, month);

    public async Task<FinanceDashboard> GetDashboardAsync(int userId, int year, int month)
    {
        var items = await GetTransactionsByMonth(userId, year, month);

        return new FinanceDashboard
        {
            Year = year,
            Month = month,
            TotalIncomes = items.Where(t => t.IsIncome).Sum(t => t.Value),
            TotalExpenses = items.Where(t => !t.IsIncome).Sum(t => t.Value),
            FixedTotal = items.Where(t => t.IsFixed && !t.IsIncome).Sum(t => t.Value),
            InstallmentTotal = items.Where(t => t.TotalInstallments > 1 && !t.IsIncome).Sum(t => t.Value),
            TransactionCount = items.Count
        };
    }

    public async Task SaveTransactionAsync(Transaction transaction)
    {
        await InitializeAsync();

        if (transaction.TotalInstallments > 1)
        {
            var parentId = Guid.NewGuid();
            var baseDate = new DateTime(transaction.Date.Year, transaction.Date.Month, 1);
            var installmentValue = Math.Round(transaction.Value / transaction.TotalInstallments, 2);
            var lastInstallmentValue = transaction.Value - (installmentValue * (transaction.TotalInstallments - 1));

            for (var i = 0; i < transaction.TotalInstallments; i++)
            {
                var installmentDate = baseDate.AddMonths(i);
                var currentValue = (i == transaction.TotalInstallments - 1) ? lastInstallmentValue : installmentValue;

                await _db!.InsertAsync(new Transaction
                {
                    UserId = transaction.UserId,
                    Description = $"{transaction.Description} ({i + 1}/{transaction.TotalInstallments})",
                    Value = currentValue,
                    Category = transaction.Category,
                    IsFixed = false,
                    InstallmentNumber = i + 1,
                    TotalInstallments = transaction.TotalInstallments,
                    ParentId = parentId,
                    Date = installmentDate,
                    Year = installmentDate.Year,
                    Month = installmentDate.Month,
                    IsIncome = transaction.IsIncome,
                    GroupId = transaction.GroupId,
                    IsPrivate = transaction.IsPrivate
                });
            }

            return;
        }

        transaction.Date = new DateTime(transaction.Date.Year, transaction.Date.Month, 1);
        transaction.Year = transaction.Date.Year;
        transaction.Month = transaction.Date.Month;
        transaction.InstallmentNumber = 1;
        transaction.TotalInstallments = 1;
        transaction.ParentId = null;

        await _db!.InsertAsync(transaction);
    }

    public Task AddAsync(Transaction transaction) => SaveTransactionAsync(transaction);

    public async Task DeleteAsync(int userId, int id)
    {
        await InitializeAsync();
        var transaction = await _db!.Table<Transaction>()
            .Where(t => t.UserId == userId && t.Id == id)
            .FirstOrDefaultAsync();
            
        if (transaction is null) return;

        if (transaction.ParentId.HasValue)
        {
            await DeleteByParentIdAsync(userId, transaction.ParentId.Value);
        }
        else
        {
            await _db.DeleteAsync(transaction);
        }
    }

    public async Task DeleteByParentIdAsync(int userId, Guid parentId)
    {
        await InitializeAsync();
        await _db!.ExecuteAsync("DELETE FROM Transactions WHERE UserId = ? AND ParentId = ?", userId, parentId);
    }

    private async Task ReplicateFixedExpensesAsync(int userId, int year, int month)
    {
        var targetMonthStart = new DateTime(year, month, 1);
        var limitDate = targetMonthStart.AddMonths(1);

        var templates = await _db!.Table<Transaction>()
            .Where(t => t.UserId == userId && t.IsFixed && t.TotalInstallments <= 1 && t.Date < limitDate)
            .ToListAsync();

        foreach (var group in templates.GroupBy(t => new { t.Description, t.Category, t.Value, t.IsIncome }))
        {
            var desc = group.Key.Description;
            var cat = group.Key.Category;
            var val = group.Key.Value;
            var isInc = group.Key.IsIncome;

            var exists = await _db.Table<Transaction>()
                .Where(t =>
                    t.UserId == userId &&
                    t.IsFixed &&
                    t.Description == desc &&
                    t.Category == cat &&
                    t.Value == val &&
                    t.IsIncome == isInc &&
                    t.Year == year &&
                    t.Month == month)
                .CountAsync();

            if (exists > 0) continue;

            var source = group.OrderBy(t => t.Id).First();
            await _db.InsertAsync(new Transaction
            {
                UserId = userId,
                Description = source.Description,
                Value = source.Value,
                Date = targetMonthStart,
                Year = year,
                Month = month,
                Category = source.Category,
                IsFixed = true,
                IsIncome = source.IsIncome,
                InstallmentNumber = 1,
                TotalInstallments = 1,
                GroupId = source.GroupId,
                IsPrivate = source.IsPrivate
            });
        }
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesByUserAsync(int userId)
    {
        await InitializeAsync();
        
        var list = await _db!.Table<Category>()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync();
            
        if (list.Count == 0)
        {
            var defaults = new List<string> { "Alimentação", "Lazer", "Contas", "Transporte", "Saúde", "Educação", "Salário", "Investimentos", "Freelance", "Geral" };
            foreach (var name in defaults)
            {
                await _db.InsertAsync(new Category
                {
                    UserId = userId,
                    Name = name
                });
            }
            
            list = await _db.Table<Category>()
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        
        return list;
    }

    public async Task SaveCategoryAsync(Category category)
    {
        await InitializeAsync();
        
        if (category.Id > 0)
        {
            await _db!.UpdateAsync(category);
        }
        else
        {
            var exists = await _db!.Table<Category>()
                .Where(c => c.UserId == category.UserId && c.Name.ToLower() == category.Name.ToLower())
                .CountAsync();
                
            if (exists == 0)
            {
                await _db.InsertAsync(category);
            }
        }
    }

    public async Task DeleteCategoryAsync(int userId, int id)
    {
        await InitializeAsync();
        await _db!.Table<Category>()
            .Where(c => c.UserId == userId && c.Id == id)
            .DeleteAsync();
    }

    public async Task<Group?> GetGroupForUserAsync(int userId)
    {
        await InitializeAsync();
        var member = await _db!.Table<GroupMember>()
            .Where(gm => gm.UserId == userId)
            .FirstOrDefaultAsync();
            
        if (member is null) return null;
        
        return await _db.Table<Group>()
            .Where(g => g.Id == member.GroupId)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<User>> GetGroupMembersAsync(int groupId)
    {
        await InitializeAsync();
        var members = await _db!.Table<GroupMember>()
            .Where(gm => gm.GroupId == groupId)
            .ToListAsync();
            
        var userIds = members.Select(gm => gm.UserId).ToList();
        var users = new List<User>();
        foreach (var id in userIds)
        {
            var u = await _db.Table<User>().Where(usr => usr.Id == id).FirstOrDefaultAsync();
            if (u is not null) users.Add(u);
        }
        return users;
    }

    public async Task CreateGroupAsync(string name, int adminId)
    {
        await InitializeAsync();
        
        var existing = await _db!.Table<GroupMember>()
            .Where(gm => gm.UserId == adminId)
            .CountAsync();
        if (existing > 0) return;
        
        var group = new Group
        {
            Name = name,
            AdminUserId = adminId
        };
        await _db.InsertAsync(group);
        
        var member = new GroupMember
        {
            GroupId = group.Id,
            UserId = adminId
        };
        await _db.InsertAsync(member);
    }

    public async Task<string?> AddMemberToGroupAsync(int groupId, string username, int adminId)
    {
        await InitializeAsync();
        
        var group = await _db!.Table<Group>().Where(g => g.Id == groupId).FirstOrDefaultAsync();
        if (group is null) return "Grupo não encontrado.";
        if (group.AdminUserId != adminId) return "Apenas o administrador do grupo pode adicionar membros.";
        
        var targetUser = await _db.Table<User>().Where(u => u.Username.ToLower() == username.ToLower()).FirstOrDefaultAsync();
        if (targetUser is null) return "Usuário não encontrado.";
        
        var alreadyMember = await _db.Table<GroupMember>().Where(gm => gm.UserId == targetUser.Id).CountAsync();
        if (alreadyMember > 0) return "O usuário já pertence a um grupo.";
        
        var member = new GroupMember
        {
            GroupId = groupId,
            UserId = targetUser.Id
        };
        await _db.InsertAsync(member);
        return null;
    }

    public async Task RemoveMemberFromGroupAsync(int groupId, int userId, int adminId)
    {
        await InitializeAsync();
        
        var group = await _db!.Table<Group>().Where(g => g.Id == groupId).FirstOrDefaultAsync();
        if (group is null) return;
        
        if (group.AdminUserId == adminId || userId == adminId)
        {
            await _db.ExecuteAsync("DELETE FROM GroupMembers WHERE GroupId = ? AND UserId = ?", groupId, userId);
        }
    }

    public async Task DeleteGroupAsync(int groupId, int adminId)
    {
        await InitializeAsync();
        
        var group = await _db!.Table<Group>().Where(g => g.Id == groupId).FirstOrDefaultAsync();
        if (group is null) return;
        if (group.AdminUserId != adminId) return;
        
        await _db.ExecuteAsync("UPDATE Transactions SET GroupId = NULL WHERE GroupId = ?", groupId);
        await _db.ExecuteAsync("DELETE FROM GroupMembers WHERE GroupId = ?", groupId);
        await _db.DeleteAsync(group);
    }

    public async Task SaveBillToPayAsync(BillToPay bill)
    {
        await InitializeAsync();

        if (bill.Id > 0)
        {
            await _db!.UpdateAsync(bill);
        }
        else
        {
            await _db!.InsertAsync(bill);
        }

        var notifyDate = bill.DueDate.AddDays(-bill.DaysBeforeToNotify);
        notifyDate = notifyDate.Date.AddHours(9);

#if ANDROID || IOS
        if (notifyDate > DateTime.Now)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            var description = $"A sua conta '{bill.Description}' no valor de R$ {bill.Value.ToString("N2", culture)} vence em {bill.DueDate.ToString("dd/MM/yyyy")}.";

            var request = new NotificationRequest
            {
                NotificationId = bill.Id,
                Title = "Alerta de Vencimento! 💸",
                Description = description,
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = notifyDate
                }
            };

            await LocalNotificationCenter.Current.Show(request);
        }
        else
        {
            LocalNotificationCenter.Current.Cancel(bill.Id);
        }
#else
        await Task.CompletedTask;
#endif
    }

    public async Task<IReadOnlyList<BillToPay>> GetBillsToPayAsync(int userId)
    {
        await InitializeAsync();
        return await _db!.Table<BillToPay>()
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.DueDate)
            .ToListAsync();
    }

    public async Task DeleteBillToPayAsync(int userId, int id)
    {
        await InitializeAsync();
        var bill = await _db!.Table<BillToPay>()
            .Where(b => b.UserId == userId && b.Id == id)
            .FirstOrDefaultAsync();

        if (bill is not null)
        {
            await _db.DeleteAsync(bill);
#if ANDROID || IOS
            LocalNotificationCenter.Current.Cancel(bill.Id);
#else
            await Task.CompletedTask;
#endif
        }
    }

    public async Task UpdateUserAsync(User user)
    {
        await InitializeAsync();
        await _db!.UpdateAsync(user);
    }

    public async Task<string> ExportTransactionsToExcelAsync(int userId, int year, int month)
    {
        var transactions = await GetTransactionsByMonth(userId, year, month);

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Transações");

            // Columns headers
            string[] headers = { "Data", "Descrição", "Categoria", "Valor", "Parcelas", "Tipo" };
            for (int col = 1; col <= headers.Length; col++)
            {
                var cell = worksheet.Cells[1, col];
                cell.Value = headers[col - 1];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(15, 23, 42)); // #0f172a
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            int row = 2;
            foreach (var tx in transactions)
            {
                worksheet.Cells[row, 1].Value = tx.Date;
                worksheet.Cells[row, 1].Style.Numberformat.Format = "dd/MM/yyyy";

                worksheet.Cells[row, 2].Value = tx.Description;
                worksheet.Cells[row, 3].Value = tx.Category;

                // Income is positive, expense is negative
                var val = tx.IsIncome ? tx.Value : -tx.Value;
                worksheet.Cells[row, 4].Value = (double)val;
                worksheet.Cells[row, 4].Style.Numberformat.Format = "R$ #,##0.00;[Red]R$ -#,##0.00";

                worksheet.Cells[row, 5].Value = $"{tx.InstallmentNumber}/{tx.TotalInstallments}";
                worksheet.Cells[row, 6].Value = tx.IsFixed ? "Fixo" : "Variável";

                row++;
            }

            // Auto-fit columns
            if (transactions.Count > 0)
            {
                worksheet.Cells[1, 1, row - 1, headers.Length].AutoFitColumns();
            }
            else
            {
                worksheet.Cells[1, 1, 1, headers.Length].AutoFitColumns();
            }

            string fileName = $"transacoes_{year}_{month:D2}.xlsx";
            string filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            var fileInfo = new FileInfo(filePath);
            await package.SaveAsAsync(fileInfo);

            return filePath;
        }
    }

    public async Task ExportAndShareTransactionsAsync(int userId, int year, int month)
    {
        var filePath = await ExportTransactionsToExcelAsync(userId, year, month);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"Exportar Transações - {month:D2}/{year}",
            File = new ShareFile(filePath)
        });
    }
}

public record FinanceDashboard
{
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal TotalIncomes { get; init; }
    public decimal TotalExpenses { get; init; }
    public decimal Balance => TotalIncomes - TotalExpenses;
    public decimal FixedTotal { get; init; }
    public decimal InstallmentTotal { get; init; }
    public int TransactionCount { get; init; }
}
