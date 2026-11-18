using Wms.Application.Demo;

namespace Wms.Application.Tests;

public class DemoStopListTests
{
    [Theory]
    [InlineData("POST", "/api/admin/users/usr_x/reset-password")]
    [InlineData("POST", "/api/admin/users/usr_x/block")]
    [InlineData("POST", "/api/admin/users/usr_x/unblock")]
    [InlineData("POST", "/api/admin/organizations/org_fc1/approve")]
    [InlineData("POST", "/api/admin/organizations/org_fc1/reject")]
    [InlineData("POST", "/api/admin/organizations/org_fc1/block")]
    [InlineData("POST", "/api/admin/organizations/org_fc1/unblock")]
    [InlineData("POST", "/api/admin/references")]
    [InlineData("POST", "/api/admin/references/ref_1/toggle")]
    [InlineData("POST", "/api/admin/demo/reset")]
    [InlineData("POST", "/api/integrations/wb/connect")]
    [InlineData("POST", "/api/integrations/ozon/disconnect")]
    [InlineData("PUT", "/api/integrations/wb")]
    [InlineData("POST", "/api/integrations/wb/sync")]
    [InlineData("PUT", "/api/settings/profile")]
    [InlineData("POST", "/api/settings/team/invite")]
    [InlineData("POST", "/api/settings/team/inv_1/revoke")]
    [InlineData("POST", "/api/fc/client-requests/creq_1/approve")]
    [InlineData("POST", "/api/seller/fulfillments/org_fc1/connect")]
    [InlineData("PUT", "/api/billing/tariffs/tar_1")]
    [InlineData("POST", "/api/warehouses/wh_kzn1/zones")]
    [InlineData("POST", "/api/warehouses/wh_kzn1/locations")]
    [InlineData("PUT", "/api/staff/usr_fc1_3")]
    [InlineData("POST", "/api/staff/usr_fc1_3/shifts")]
    [InlineData("POST", "/api/catalog/products/prd_1/archive")]
    [InlineData("POST", "/api/analytics/slotting/apply")]
    [InlineData("POST", "/api/stock/writeoff")]
    [InlineData("POST", "/api/tasks/tsk_1/cancel")]
    public void Blocks_stop_list_mutations(string method, string path) =>
        Assert.True(DemoStopList.IsBlocked(method, path));

    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/admin/overview")]
    [InlineData("GET", "/api/auth/demo/status")]
    [InlineData("POST", "/api/auth/demo")]
    [InlineData("POST", "/api/auth/login")]
    [InlineData("POST", "/api/work/tasks/tsk_1/start")]
    [InlineData("POST", "/api/notifications/read-all")]
    [InlineData("POST", "/api/catalog/products/prd_1/restore")]
    [InlineData("POST", "/api/billing/acts/act_1/issue")]
    [InlineData("POST", "/api/tasks/tsk_1/assign")]
    [InlineData("GET", "/api/tasks")]
    [InlineData("PUT", "/api/settings/notifications")]
    public void Allows_other_routes(string method, string path) =>
        Assert.False(DemoStopList.IsBlocked(method, path));
}
