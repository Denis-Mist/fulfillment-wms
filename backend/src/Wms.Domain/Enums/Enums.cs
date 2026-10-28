namespace Wms.Domain.Enums;

public enum Role
{
    PlatformAdmin, FcOwner, FcManager, FcSupervisor, FcAccountManager,
    FcReceiver, FcPicker, FcPacker, SellerOwner, SellerManager,
}

public enum OrganizationType { Fulfillment, Seller }

public enum OrganizationStatus { Pending, Active, Rejected, Blocked }

public enum Marketplace { Wb, Ozon, Yandex }
