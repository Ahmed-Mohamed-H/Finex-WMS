using Finex.Domain.Enums.Catalog;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Domain.ValueObjects.Catalog
{
    public sealed record CategoryAttributeDefinition
    {
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
                throw new ArgumentException("Attribute key cannot be null or whitespace.", nameof(key));

            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("Attribute label cannot be null or whitespace.", nameof(label));

            if (!Enum.IsDefined(dataType))
                throw new ArgumentOutOfRangeException(nameof(dataType), "Invalid attribute data type.");

            Key = key.Trim();
            Label = label.Trim();
            DataType = dataType;
            IsRequired = isRequired;
            Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();

            if (allowedValues is not null)
            {
                var normalizedAllowed = allowedValues
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (normalizedAllowed.Count == 0)
                    throw new ArgumentException("AllowedValues cannot be empty when specified.", nameof(allowedValues));

                AllowedValues = normalizedAllowed.AsReadOnly();
            }
        }
    }
}
