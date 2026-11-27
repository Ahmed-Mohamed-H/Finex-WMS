using Finex.Application.Services.Catalog;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.Interfaces.Catalog
{
    public interface ICategoryCatalogCache
    {
        ValueTask<CategoryCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default);

        Task RefreshProactivelyAsync(CancellationToken ct = default);
    }
}
