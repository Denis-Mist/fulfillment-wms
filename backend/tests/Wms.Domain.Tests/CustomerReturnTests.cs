using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class CustomerReturnTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static CustomerReturn Expected() => new("ret_1", "ВЗ-1", "ord_1", ReturnReason.Size, ReturnStatus.Expected, "org_s", "wh_1",
        Now, null, null, null, null, 0, [new ReturnLine("p1", 2), new ReturnLine("p2", 1)]);

    [Fact]
    public void Lifecycle_grades_lines_and_returns_restock()
    {
        var r = Expected();
        Assert.Null(r.Resolution);
        Assert.Throws<DomainConflictException>(() => r.Inspect(new Dictionary<string, ReturnGrade>(), null, "x", Now));
        r.Receive(Now);
        Assert.Throws<DomainConflictException>(() => r.Receive(Now));
        var e = Assert.Throws<DomainValidationException>(() => r.Inspect(new Dictionary<string, ReturnGrade> { ["p1"] = ReturnGrade.AsNew }, null, "x", Now));
        Assert.Equal("Оцените все позиции", e.Message);
        Assert.Throws<DomainConflictException>(() => r.Close());
        r.Inspect(new Dictionary<string, ReturnGrade> { ["p1"] = ReturnGrade.AsNew, ["p2"] = ReturnGrade.Dispose }, "  ок ", "Ольга", Now);
        Assert.Equal(ReturnResolution.Mixed, r.Resolution);
        Assert.Equal("ок", r.Comment);
        Assert.Equal(1, r.Photos);
        var restock = Assert.Single(r.Close());
        Assert.Equal("p1", restock.ProductId);
        Assert.Equal(ReturnStatus.Closed, r.Status);
        Assert.Equal(3, r.Units);
    }

    [Fact]
    public void Resolution_is_restocked_or_defect_when_uniform()
    {
        var r = Expected();
        r.Receive(Now);
        r.Inspect(new Dictionary<string, ReturnGrade> { ["p1"] = ReturnGrade.Defect, ["p2"] = ReturnGrade.Dispose }, null, "x", Now);
        Assert.Equal(ReturnResolution.Defect, r.Resolution);
        Assert.Empty(r.Close());
    }
}
