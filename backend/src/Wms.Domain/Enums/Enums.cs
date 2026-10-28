namespace Wms.Domain.Enums;

public enum Role
{
    PlatformAdmin, FcOwner, FcManager, FcSupervisor, FcAccountManager,
    FcReceiver, FcPicker, FcPacker, SellerOwner, SellerManager,
}

public enum OrganizationType { Fulfillment, Seller }

public enum OrganizationStatus { Pending, Active, Rejected, Blocked }

public enum Marketplace { Wb, Ozon, Yandex }

public enum ModerationAction { Approve, Reject, Block, Unblock }

public enum ReferenceKind { Units, CellTypes, Carriers, Services }

public enum AuditCategory { Auth, Organization, Inbound, Order, Stock, Reference, User }

public enum ClientRequestStatus { Pending, Accepted, Declined }
