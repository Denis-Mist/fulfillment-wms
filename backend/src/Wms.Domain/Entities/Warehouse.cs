using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public class Warehouse
{
    private Warehouse() { }

    public Warehouse(string id, string organizationId, string name, string code, string city, string address, int areaSqm, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new DomainException("Не задан идентификатор склада");
        Id = id; OrganizationId = organizationId; Name = name; Code = code; City = city;
        Address = address; AreaSqm = areaSqm; IsActive = isActive;
    }

    public string Id { get; private set; } = null!;
    public string OrganizationId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public int AreaSqm { get; private set; }
    public bool IsActive { get; private set; }
}
