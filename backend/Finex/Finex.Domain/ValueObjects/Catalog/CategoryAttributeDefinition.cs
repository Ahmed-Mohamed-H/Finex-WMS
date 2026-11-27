using System.Text.RegularExpressions;
using Finex.Domain.Enums.Catalog;

namespace Finex.Domain.ValueObjects.Catalog;

public sealed record CategoryAttributeDefinition
{
    public const int MaxKeyLength = 50;
    public const int MaxLabelLength = 100;
    public const int MaxUnitLength = 20;
    public const int MaxAllowedValuesCount = 50;
    public const int MaxAllowedValueLength = 100;

    // يبدأ بحرف إنجليزي، ويحتوي على حروف إنجليزي وأرقام و _ فقط (آمن تماماً لمسارات SQL JSON)
    private static readonly Regex ValidKeyRegex = new(
        "^[a-zA-Z][a-zA-Z0-9_]{1,49}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Key { get; }
    public string Label { get; }
    public AttributeDataType DataType { get; }
    public bool IsRequired { get; }
    public string? Unit { get; }
    public IReadOnlyList<string>? AllowedValues { get; }

    private CategoryAttributeDefinition()
    {
        Key = string.Empty;
        Label = string.Empty;
    }

    public CategoryAttributeDefinition(
        string key,
        string label,
        AttributeDataType dataType,
        bool isRequired,
        string? unit = null,
        IEnumerable<string>? allowedValues = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Attribute key cannot be empty.", nameof(key));

        var trimmedKey = key.Trim();
        if (trimmedKey.Length > MaxKeyLength || !ValidKeyRegex.IsMatch(trimmedKey))
            throw new ArgumentException(
                $"Attribute key '{trimmedKey}' must be 2-{MaxKeyLength} chars, start with an English letter, and contain only letters, numbers, or underscores.",
                nameof(key));

        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Attribute label cannot be empty.", nameof(label));

        var trimmedLabel = label.Trim();
        if (trimmedLabel.Length > MaxLabelLength)
            throw new ArgumentException($"Attribute label cannot exceed {MaxLabelLength} characters.", nameof(label));

        if (!Enum.IsDefined(dataType))
            throw new ArgumentOutOfRangeException(nameof(dataType), "Invalid attribute data type.");

        string? trimmedUnit = null;
        if (!string.IsNullOrWhiteSpace(unit))
        {
            trimmedUnit = unit.Trim();
            if (trimmedUnit.Length > MaxUnitLength)
                throw new ArgumentException($"Attribute unit cannot exceed {MaxUnitLength} characters.", nameof(unit));
        }

        Key = trimmedKey;
        Label = trimmedLabel;
        DataType = dataType;
        IsRequired = isRequired;
        Unit = trimmedUnit;

        if (allowedValues is not null)
        {
            if (dataType != AttributeDataType.String)
                throw new ArgumentException("AllowedValues can only be defined for String attributes.", nameof(allowedValues));

            var normalizedAllowed = allowedValues
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedAllowed.Count == 0)
                throw new ArgumentException("AllowedValues cannot be empty when specified.", nameof(allowedValues));

            if (normalizedAllowed.Count > MaxAllowedValuesCount)
                throw new ArgumentException($"AllowedValues cannot exceed {MaxAllowedValuesCount} options.", nameof(allowedValues));

            if (normalizedAllowed.Any(v => v.Length > MaxAllowedValueLength))
                throw new ArgumentException($"Each option in AllowedValues cannot exceed {MaxAllowedValueLength} characters.", nameof(allowedValues));

            AllowedValues = normalizedAllowed.AsReadOnly();
        }
    }
}