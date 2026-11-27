using Finex.Domain.Enums.Catalog;

namespace Finex.Application.DTOs.Catalog;

public sealed record AttributeDefinitionDto(
    string Key,
    string Label,
    AttributeDataType DataType,
    bool IsRequired,
    string? Unit = null,
    IReadOnlyList<string>? AllowedValues = null);