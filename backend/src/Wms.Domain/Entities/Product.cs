using System.Text.RegularExpressions;
using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>Данные карточки товара от пользователя (форма или строка импорта).</summary>
public sealed record ProductDraft(string? Sku, string? Name, string? Category, string? Barcode, string? WbBarcode,
    double? Price, double? WeightG, double? VolumeLiters, bool? TracksExpiry, bool? RequiresMarking)
{
    public string NormalizedSku => (Sku ?? string.Empty).Trim();
}

/// <summary>Товар селлера.</summary>
public partial class Product
{
    public const string DefaultCategory = "Без категории";

    private Product() { }

    public Product(string id, string sellerId, string sku, string name, string category, string barcode, string? wbBarcode,
        double price, double weightG, double volumeLiters, bool tracksExpiry, bool requiresMarking)
    {
        Id = id; SellerId = sellerId;
        Set(sku, name, category, barcode, wbBarcode, price, weightG, volumeLiters, tracksExpiry, requiresMarking);
    }

    public string Id { get; private set; } = null!;
    public string SellerId { get; private set; } = null!;
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string Barcode { get; private set; } = null!;
    public string? WbBarcode { get; private set; }
    public double Price { get; private set; }
    public double WeightG { get; private set; }
    public double VolumeLiters { get; private set; }
    public bool TracksExpiry { get; private set; }
    public bool RequiresMarking { get; private set; }
    public bool Archived { get; private set; }

    [GeneratedRegex("^[A-Za-z0-9-_.]{2,32}$")]
    private static partial Regex SkuPattern();

    [GeneratedRegex(@"^\d{8}$|^\d{13}$")]
    private static partial Regex BarcodePattern();

    [GeneratedRegex(@"^\d{8,14}$")]
    private static partial Regex MarketplaceBarcodePattern();

    /// <summary>Проверка формата карточки (без уникальности) в порядке моков; null — без ошибок.</summary>
    public static string? Validate(ProductDraft d)
    {
        var sku = d.NormalizedSku;
        if (sku.Length == 0) return "Укажите артикул";
        if (!SkuPattern().IsMatch(sku)) return "Артикул: 2–32 символа, латиница, цифры, «-», «_», «.»";
        if ((d.Name ?? string.Empty).Trim().Length < 3) return "Название не короче 3 символов";
        if (!BarcodePattern().IsMatch(d.Barcode ?? string.Empty)) return "Штрихкод должен содержать 8 или 13 цифр";
        if (!string.IsNullOrEmpty(d.WbBarcode) && !MarketplaceBarcodePattern().IsMatch(d.WbBarcode)) return "ШК маркетплейса: 8–14 цифр";
        if (!(d.Price > 0)) return "Цена должна быть больше нуля";
        if (!(d.WeightG > 0) || !(d.VolumeLiters > 0)) return "Укажите вес и объём";
        return null;
    }

    public bool ConflictsWith(ProductDraft d) =>
        string.Equals(Sku, d.NormalizedSku, StringComparison.OrdinalIgnoreCase) || Barcode == d.Barcode;

    public static Product Create(string id, string sellerId, ProductDraft d)
    {
        EnsureValid(d);
        var product = new Product { Id = id, SellerId = sellerId };
        product.Apply(d);
        return product;
    }

    public void Update(ProductDraft d)
    {
        EnsureValid(d);
        Apply(d);
    }

    /// <summary>Архивировать можно только товар без остатка на складах.</summary>
    public void Archive(int stockQty)
    {
        if (stockQty > 0) throw new DomainConflictException("Нельзя архивировать товар с остатком");
        Archived = true;
    }

    public void Restore() => Archived = false;

    private static void EnsureValid(ProductDraft d)
    {
        if (Validate(d) is { } error) throw new DomainException(error);
    }

    private void Apply(ProductDraft d) => Set(d.NormalizedSku, d.Name!.Trim(),
        string.IsNullOrWhiteSpace(d.Category) ? DefaultCategory : d.Category.Trim(), d.Barcode!,
        string.IsNullOrEmpty(d.WbBarcode) ? null : d.WbBarcode, d.Price!.Value, d.WeightG!.Value, d.VolumeLiters!.Value,
        d.TracksExpiry ?? false, d.RequiresMarking ?? false);

    private void Set(string sku, string name, string category, string barcode, string? wbBarcode, double price, double weightG,
        double volumeLiters, bool tracksExpiry, bool requiresMarking)
    {
        Sku = sku; Name = name; Category = category; Barcode = barcode; WbBarcode = wbBarcode; Price = price;
        WeightG = weightG; VolumeLiters = volumeLiters; TracksExpiry = tracksExpiry; RequiresMarking = requiresMarking;
    }
}
