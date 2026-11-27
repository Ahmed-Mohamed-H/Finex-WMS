namespace Finex.Domain.ValueObjects.Catalog;

public sealed record CategoryAttributeSchema
{
    public const int MaxAttributesPerCategory = 30;

    public IReadOnlyList<CategoryAttributeDefinition> Attributes { get; }

    private CategoryAttributeSchema()
    {
        Attributes = [];
    }

    public CategoryAttributeSchema(IEnumerable<CategoryAttributeDefinition> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var list = attributes.ToList();
        if (list.Count == 0)
            throw new ArgumentException("Schema must contain at least one attribute definition.", nameof(attributes));

        if (list.Count > MaxAttributesPerCategory)
            throw new ArgumentException($"Schema cannot contain more than {MaxAttributesPerCategory} attributes.", nameof(attributes));

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in list)
        {
            if (!seenKeys.Add(attr.Key))
                throw new ArgumentException($"Duplicate attribute key '{attr.Key}' in category schema.", nameof(attributes));
        }

        Attributes = list.AsReadOnly();
    }
}