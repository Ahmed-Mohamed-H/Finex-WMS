using Finex.Application.Services.Catalog;

namespace Finex.Application.Interfaces.Catalog
{
    public interface ICategoryCatalogCache
    {
        ValueTask<CategoryCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default);

        Task RefreshProactivelyAsync(CancellationToken ct = default);
    }
}