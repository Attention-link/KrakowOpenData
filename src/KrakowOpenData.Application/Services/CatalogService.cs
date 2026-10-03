using KrakowOpenData.Application.Catalog;
using KrakowOpenData.Application.Contracts;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Services;

/// <summary>Lists the datasets the solution knows about, grouped by category.</summary>
public sealed class CatalogService
{
    public IReadOnlyList<CategoryDto> GetCategories() =>
        Enum.GetValues<DataCategory>()
            .Select(category => new CategoryDto(
                category.ToString(),
                DataCatalog.DescribeCategory(category),
                DataCatalog.Datasets.Where(d => d.Category == category).Select(d => d.ToDto()).ToList()))
            .ToList();

    public DatasetDto? GetDataset(string key) => DataCatalog.Find(key)?.ToDto();
}
