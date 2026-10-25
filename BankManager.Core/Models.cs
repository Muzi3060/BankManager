using System.Globalization;

namespace BankManager.Core;

public enum SplitMode { Individual, Equal, AvailableIncome }
public sealed record Person
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
}
public sealed record MonthlyIncome
{
    public Guid PersonId { get; set; }
    public DateOnly Month { get; set; }
    public decimal Amount { get; set; }
}
public sealed record ExpenseItem
{
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}
public sealed record Expense
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Outros";
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal Amount { get; set; }
    public bool Paid { get; set; }
    public SplitMode Split { get; set; }
    public List<Guid> Participants { get; set; } = [];
    public List<ExpenseItem> Items { get; set; } = [];
    public Guid? InstallmentGroup { get; set; }
    public int InstallmentNumber { get; set; } = 1;
    public int InstallmentCount { get; set; } = 1;
}
public sealed record BudgetData
{
    public int Version { get; set; } = 1;
    public List<Person> People { get; set; } = [];
    public List<MonthlyIncome> Incomes { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
}
public static class Money
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");
    public static string Format(decimal value) => value.ToString("C2", Culture);
    public static decimal Parse(string? input, string label, bool positive = false)
    {
        var s = input?.Trim() ?? "";
        // Unambiguous Brazilian input; reject excess precision instead of silently rounding.
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{1,9}(,\d{1,2})?$") ||
            !decimal.TryParse(s, NumberStyles.AllowDecimalPoint, Culture, out var value))
            throw new BudgetException($"{label}: use um número como 1500,50, sem pontos ou símbolo R$.");
        Validate(value, label, positive);
        return value;
    }
    public static void Validate(decimal value, string label, bool positive = false)
    {
        if (value < 0 || value > 999_999_999.99m || (positive && value == 0) || decimal.Round(value, 2) != value)
            throw new BudgetException($"{label}: informe {(positive ? "um valor maior que zero" : "um valor a partir de zero")}, com até 2 casas decimais e no máximo R$ 999.999.999,99.");
    }
}
public sealed class BudgetException(string message) : Exception(message);
public static class Dates
{
    public static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);
}
