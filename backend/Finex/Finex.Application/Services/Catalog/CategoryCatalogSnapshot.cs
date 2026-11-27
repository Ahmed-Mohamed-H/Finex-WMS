using Finex.Application.DTOs.Catalog;
using System.Collections.Frozen;

namespace Finex.Application.Services.Catalog
{
    public sealed class CategoryCatalogSnapshot
    {
        public static readonly CategoryCatalogSnapshot Empty = Create([]);

        public DateTimeOffset BuiltAtUtc { get; }
        public FrozenDictionary<int, CategorySnapshotNode> ById { get; }
        public FrozenDictionary<string, int> IdByCode { get; }
        public FrozenDictionary<int, EffectiveCategorySchemaDto?> EffectiveSchemaById { get; }
        public IReadOnlyList<CategoryTreeNodeDto> ActiveTree { get; }
        public IReadOnlyList<CategoryTreeNodeDto> AllTree { get; }

        private CategoryCatalogSnapshot(
            DateTimeOffset builtAtUtc,
            FrozenDictionary<int, CategorySnapshotNode> byId,
            FrozenDictionary<string, int> idByCode,
            FrozenDictionary<int, EffectiveCategorySchemaDto?> effectiveSchemaById,
            IReadOnlyList<CategoryTreeNodeDto> activeTree,
            IReadOnlyList<CategoryTreeNodeDto> allTree)
        {
            BuiltAtUtc = builtAtUtc;
            ById = byId;
            IdByCode = idByCode;
            EffectiveSchemaById = effectiveSchemaById;
            ActiveTree = activeTree;
            AllTree = allTree;
        }

        public static CategoryCatalogSnapshot Create(IReadOnlyList<CategorySnapshotNode> nodes)
        {
            var now = DateTimeOffset.UtcNow;

            if (nodes.Count == 0)
            {
                return new CategoryCatalogSnapshot(
                    now,
                    FrozenDictionary<int, CategorySnapshotNode>.Empty,
                    FrozenDictionary<string, int>.Empty,
                    FrozenDictionary<int, EffectiveCategorySchemaDto?>.Empty,
                    [],
                    []);
            }

            var byIdDict = new Dictionary<int, CategorySnapshotNode>(nodes.Count);
            var idByCodeDict = new Dictionary<string, int>(nodes.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var node in nodes)
            {
                byIdDict[node.Id] = node;
                idByCodeDict[node.Code] = node.Id;
            }

            var byId = byIdDict.ToFrozenDictionary();
            var idByCode = idByCodeDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            var effectiveSchemas = BuildEffectiveSchemas(nodes, byId);
            var activeTree = BuildTree(nodes, includeInactive: false);
            var allTree = BuildTree(nodes, includeInactive: true);

            return new CategoryCatalogSnapshot(
                now,
                byId,
                idByCode,
                effectiveSchemas,
                activeTree,
                allTree);
        }

        private static FrozenDictionary<int, EffectiveCategorySchemaDto?> BuildEffectiveSchemas(
            IReadOnlyList<CategorySnapshotNode> nodes,
            FrozenDictionary<int, CategorySnapshotNode> byId)
        {
            var mappedAttributesByOwnerId = new Dictionary<int, IReadOnlyList<AttributeDefinitionDto>>();
            foreach (var node in nodes)
            {
                if (node.AttributeSchema is not null)
                {
                    mappedAttributesByOwnerId[node.Id] = node.AttributeSchema.Attributes
                        .Select(a => new AttributeDefinitionDto(
                            a.Key,
                            a.Label,
                            a.DataType,
                            a.IsRequired,
                            a.Unit,
                            a.AllowedValues))
                        .ToList()
                        .AsReadOnly();
                }
            }

            var result = new Dictionary<int, EffectiveCategorySchemaDto?>(nodes.Count);

            foreach (var node in nodes)
            {
                CategorySnapshotNode? schemaOwner = null;
                var current = node;
                var visited = new HashSet<int>();

                while (current is not null && visited.Add(current.Id))
                {
                    if (current.AttributeSchema is not null)
                    {
                        schemaOwner = current;
                        break;
                    }

                    current = current.ParentCategoryId.HasValue &&
                              byId.TryGetValue(current.ParentCategoryId.Value, out var parent)
                        ? parent
                        : null;
                }

                result[node.Id] = schemaOwner is not null &&
                                  mappedAttributesByOwnerId.TryGetValue(schemaOwner.Id, out var attrs)
                    ? new EffectiveCategorySchemaDto(
                        CategoryId: node.Id,
                        CategoryCode: node.Code,
                        CategoryName: node.Name,
                        SourceRootCategoryId: schemaOwner.Id,
                        SourceRootCategoryCode: schemaOwner.Code,
                        SourceRootCategoryName: schemaOwner.Name,
                        Attributes: attrs)
                    : null;
            }

            return result.ToFrozenDictionary();
        }

        private static IReadOnlyList<CategoryTreeNodeDto> BuildTree(
            IReadOnlyList<CategorySnapshotNode> nodes,
            bool includeInactive)
        {
            var filtered = includeInactive
                ? nodes
                : nodes.Where(n => n.IsActive).ToList();

            if (filtered.Count == 0)
                return [];

            var builders = new Dictionary<int, MutableNode>(filtered.Count);
            var roots = new List<MutableNode>();

            foreach (var node in filtered)
            {
                builders[node.Id] = new MutableNode(node);
            }

            foreach (var node in filtered)
            {
                var current = builders[node.Id];

                if (node.ParentCategoryId is null)
                {
                    roots.Add(current);
                }
                else if (builders.TryGetValue(node.ParentCategoryId.Value, out var parentBuilder))
                {
                    parentBuilder.Children.Add(current);
                }
            }

            return roots.Select(ToDto).ToList().AsReadOnly();
        }

        private static CategoryTreeNodeDto ToDto(MutableNode node)
        {
            var children = node.Children.Count == 0
                ? (IReadOnlyList<CategoryTreeNodeDto>)[]
                : node.Children.Select(ToDto).ToList().AsReadOnly();

            return new CategoryTreeNodeDto(
                node.Data.Id,
                node.Data.Code,
                node.Data.Name,
                node.Data.Description,
                node.Data.Level,
                node.Data.IsActive,
                node.Data.ParentCategoryId,
                children)
            {
                HasOwnSchema = node.Data.AttributeSchema is not null
            };
        }

        private sealed class MutableNode(CategorySnapshotNode data)
        {
            public CategorySnapshotNode Data { get; } = data;
            public List<MutableNode> Children { get; } = [];
        }
    }
}