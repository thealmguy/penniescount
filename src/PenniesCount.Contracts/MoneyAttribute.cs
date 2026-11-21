using System.ComponentModel.DataAnnotations;

namespace PenniesCount.Contracts;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MoneyAttribute(bool positive = false) : ValidationAttribute
{
    public const decimal Maximum = 9999999999999999.99m;

    public override bool IsValid(object? value) =>
        value is decimal amount && amount <= Maximum
        && (positive ? amount > 0 : amount >= 0)
        && decimal.Round(amount, 2) == amount;

    public override string FormatErrorMessage(string name) =>
        $"{name} must be {(positive ? "positive" : "nonnegative")}, within the supported money range, and have at most two decimal places.";
}
