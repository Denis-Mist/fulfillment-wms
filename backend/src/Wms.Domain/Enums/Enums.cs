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

public enum ZoneType { Receiving, Storage, Picking, Packing, Shipping, Defect }

public enum InboundStatus { Draft, InTransit, Arrived, Receiving, Received, Placed, Cancelled }

public enum PackageType { Boxes, Pallets }

public enum InboundEventType { Draft, InTransit, Arrived, Receiving, Received, Placed, Cancelled, Discrepancy, Comment }

public enum InboundAction { Arrive, StartReceiving, CompleteReceiving, CompletePlacement, Cancel }

public enum TaskType { Receiving, Putaway, Picking, Packing, Move, Inventory }

public enum WarehouseTaskStatus { New, Assigned, InProgress, Done, Cancelled }

public enum TaskPriority { Normal, High }

public enum StockStatus { Available, Reserved, Defect }

public enum OperationType { Move, Inventory, Writeoff }

public enum ErrorReason { Defect, Shortage, Missort, Expired }

public enum OrderStatus { New, Reserved, OutOfStock, Picking, Picked, Packed, Shipped, Cancelled }

public enum SalesChannel { Wb, Ozon, Own }

public enum Shift
{
    Day,
    Night,
    Off,
    Vacation,
}

public enum Skill
{
    Receiving,
    Picking,
    Packing,
    Marking,
    Forklift,
    Inventory,
    Returns,
}

public enum FboStatus
{
    Draft,
    Confirmed,
    Assembling,
    Ready,
    Shipped,
    Cancelled,
}

public enum FboMarketplace
{
    Wb,
    Ozon,
}
