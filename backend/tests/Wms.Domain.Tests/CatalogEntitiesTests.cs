using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Domain.Tests;

public class ZoneAndLocationTests
{
    [Fact]
    public void Zone_code_is_normalized_and_validated()
    {
        var zone = Zone.Create("wh_1", " stg2 ", " Хранение ", ZoneType.Storage);
        Assert.Equal(("wh_1_z_STG2", "STG2", "Хранение"), (zone.Id, zone.Code, zone.Name));
        Assert.True(zone.AllowsStorage);
        Assert.False(Zone.Create("wh_1", "RCV", "П", ZoneType.Receiving).AllowsStorage);
        Assert.Equal("Код зоны: 2–6 латинских букв или цифр", Assert.Throws<DomainException>(() => Zone.Create("wh_1", "Z", "x", ZoneType.Storage)).Message);
    }

    [Fact]
    public void Location_code_and_coordinates_match_mock_formula()
    {
        var l = StorageLocation.Create("wh_1", "z", "c", 3, 2, 150);
        Assert.Equal(("wh_1_l_C-03-2", "C-03-2"), (l.Id, l.Code));
        Assert.Equal((10.4, 5.8), (l.X, l.Y));
        Assert.Equal(("C", 3), (l.Aisle, l.Rack));
    }

    [Theory]
    [InlineData("1", 1, 1, 10, "Проход: 1–2 латинские буквы")]
    [InlineData("A", 0, 1, 10, "Проверьте стеллаж, ярус и вместимость")]
    [InlineData("A", 1, 1, 0, "Проверьте стеллаж, ярус и вместимость")]
    public void Location_validation_messages(string aisle, int rack, int level, double cap, string message) =>
        Assert.Equal(message, Assert.Throws<DomainException>(() => StorageLocation.Create("wh", "z", aisle, rack, level, cap)).Message);

    [Fact]
    public void JsMath_rounds_half_up() => Assert.Equal((3.0, -2.0, 0.3), (JsMath.Round(2.5), JsMath.Round(-2.5), JsMath.Round1(0.25)));
}

public class ProductTests
{
    private static ProductDraft Draft(string sku = "SKU-1", string barcode = "4600000000001", double price = 100) =>
        new(sku, "Товар", null, barcode, null, price, 500, 1.5, null, true);

    [Fact]
    public void Create_applies_defaults_and_trims()
    {
        var p = Product.Create("prd_1", "org_s", Draft(" SKU-1 "));
        Assert.Equal(("SKU-1", Product.DefaultCategory, false, true, false), (p.Sku, p.Category, p.TracksExpiry, p.RequiresMarking, p.Archived));
    }

    [Theory]
    [InlineData("", "4600000000001", 1, "Укажите артикул")]
    [InlineData("й", "4600000000001", 1, "Артикул: 2–32 символа, латиница, цифры, «-», «_», «.»")]
    [InlineData("SKU", "123", 1, "Штрихкод должен содержать 8 или 13 цифр")]
    [InlineData("SKU", "12345678", 0, "Цена должна быть больше нуля")]
    public void Validation_follows_mock_order(string sku, string barcode, double price, string message) =>
        Assert.Equal(message, Product.Validate(Draft(sku, barcode, price)));

    [Fact]
    public void Conflict_is_case_insensitive_sku_or_same_barcode()
    {
        var p = Product.Create("prd_1", "org_s", Draft());
        Assert.True(p.ConflictsWith(Draft("sku-1", "12345678")));
        Assert.True(p.ConflictsWith(Draft("OTHER", "4600000000001")));
        Assert.False(p.ConflictsWith(Draft("OTHER", "12345678")));
    }

    [Fact]
    public void Archive_requires_empty_stock()
    {
        var p = Product.Create("prd_1", "org_s", Draft());
        Assert.Throws<DomainConflictException>(() => p.Archive(3));
        p.Archive(0);
        Assert.True(p.Archived);
        p.Restore();
        Assert.False(p.Archived);
    }
}

public class PutawayPolicyTests
{
    private static readonly Zone Storage = new("z1", "wh", "STG", "Хранение", ZoneType.Storage);
    private static readonly Zone Receiving = new("z2", "wh", "RCV", "Приёмка", ZoneType.Receiving);

    private static Product Item(double weightG = 500, double volume = 2) =>
        new("prd_1", "org_s", "SKU", "Товар", "К", "12345678", null, 10, weightG, volume, false, false);

    [Fact]
    public void All_checks_pass_for_fitting_item()
    {
        var location = new StorageLocation("l1", "wh", "z1", "A-01-3", 4, 3, 3, 100);
        var d = PutawayPolicy.Evaluate(location, Storage, Item(), 10, [new LocationStock("org_s", 5, 2)], contracted: true);
        Assert.True(d.Allowed);
        Assert.Equal(70, d.FreeLitersAfter);
        Assert.Equal("Нужно 20.0 л, свободно 90.0 л из 100 л", d.Checks[2].Detail);
        Assert.Equal("Масса единицы 0.5 кг", d.Checks[3].Detail);
    }

    [Fact]
    public void Failing_checks_report_mock_details()
    {
        var location = new StorageLocation("l1", "wh", "z2", "A-01-3", 4, 3, 3, 10);
        var d = PutawayPolicy.Evaluate(location, Receiving, Item(12_000, 4), 5, [new LocationStock("org_x", 1, 3)], contracted: false);
        Assert.False(d.Allowed);
        Assert.All(d.Checks, c => Assert.False(c.Passed));
        Assert.Equal("Приёмка не предназначена для хранения", d.Checks[1].Detail);
        Assert.Equal("Тяжёлый товар (12.0 кг) — только ярусы 1–2", d.Checks[3].Detail);
        Assert.Equal(-13, d.FreeLitersAfter);
    }
}
