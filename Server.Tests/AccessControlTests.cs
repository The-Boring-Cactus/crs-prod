using Newtonsoft.Json.Linq;
using Server.Core;

namespace Server.Tests;

[Collection("Database")]
public class AccessControlTests
{
    private readonly DatabaseFixture _fixture;

    public AccessControlTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_user_without_a_grant_cannot_see_another_users_dashboard()
    {
        if (_fixture.SkipReason != null) return;
        var owner = await _fixture.CreateUserAsync();
        var stranger = await _fixture.CreateUserAsync();

        var dashboard = new JObject { ["name"] = "Private" };
        DatabasePersistence.SaveEntity(owner, "Dashboards", dashboard);

        Assert.Empty(DatabasePersistence.LoadEntities(stranger, "Dashboards"));
    }

    [Fact]
    public async Task A_direct_grant_makes_the_dashboard_visible_to_the_grantee()
    {
        if (_fixture.SkipReason != null) return;
        var owner = await _fixture.CreateUserAsync();
        var grantee = await _fixture.CreateUserAsync();

        var dashboard = new JObject { ["name"] = "Shared with grantee" };
        DatabasePersistence.SaveEntity(owner, "Dashboards", dashboard);
        var dashId = dashboard["id"]!.ToString()!;

        DatabasePersistence.SaveResourceGrant("Dashboards", dashId, grantee, "view", owner);

        var visible = DatabasePersistence.LoadEntities(grantee, "Dashboards");
        Assert.Single(visible);
        Assert.Equal(dashId, visible[0]["id"]!.ToString());
    }

    [Fact]
    public async Task RevokeResourceGrant_removes_visibility_again()
    {
        if (_fixture.SkipReason != null) return;
        var owner = await _fixture.CreateUserAsync();
        var grantee = await _fixture.CreateUserAsync();

        var dashboard = new JObject { ["name"] = "Temporarily shared" };
        DatabasePersistence.SaveEntity(owner, "Dashboards", dashboard);
        var dashId = dashboard["id"]!.ToString()!;

        DatabasePersistence.SaveResourceGrant("Dashboards", dashId, grantee, "view", owner);
        Assert.Single(DatabasePersistence.LoadEntities(grantee, "Dashboards"));

        DatabasePersistence.RevokeResourceGrant("Dashboards", dashId, grantee);
        Assert.Empty(DatabasePersistence.LoadEntities(grantee, "Dashboards"));
    }

    [Fact]
    public async Task Project_membership_cascades_visibility_to_dashboards_inside_the_project()
    {
        if (_fixture.SkipReason != null) return;
        var owner = await _fixture.CreateUserAsync();
        var member = await _fixture.CreateUserAsync();

        var project = new JObject { ["name"] = "Shared Project" };
        DatabasePersistence.SaveProject(owner, project);
        var projectId = project["id"]!.ToString()!;

        var dashboard = new JObject { ["name"] = "Inside the project", ["projectId"] = projectId };
        DatabasePersistence.SaveEntity(owner, "Dashboards", dashboard);

        // Member has no direct grant on the dashboard itself, only project membership.
        Assert.Empty(DatabasePersistence.LoadEntities(member, "Dashboards"));

        DatabasePersistence.SaveResourceGrant("Projects", projectId, member, "view", owner);

        var visible = DatabasePersistence.LoadEntities(member, "Dashboards");
        Assert.Single(visible);
        Assert.Equal("Inside the project", visible[0]["name"]!.ToString());
    }

    [Fact]
    public async Task HasEditGrant_is_true_only_for_edit_permission_not_view()
    {
        if (_fixture.SkipReason != null) return;
        var owner = await _fixture.CreateUserAsync();
        var viewer = await _fixture.CreateUserAsync();
        var editor = await _fixture.CreateUserAsync();

        var dashboard = new JObject { ["name"] = "Permission levels" };
        DatabasePersistence.SaveEntity(owner, "Dashboards", dashboard);
        var dashId = dashboard["id"]!.ToString()!;

        DatabasePersistence.SaveResourceGrant("Dashboards", dashId, viewer, "view", owner);
        DatabasePersistence.SaveResourceGrant("Dashboards", dashId, editor, "edit", owner);

        Assert.False(DatabasePersistence.HasEditGrant(viewer, "Dashboards", dashId, null));
        Assert.True(DatabasePersistence.HasEditGrant(editor, "Dashboards", dashId, null));
    }

    [Fact]
    public async Task LoadResourceGrants_lists_grantees_with_their_user_info()
    {
        if (_fixture.SkipReason != null) return;
        var owner = await _fixture.CreateUserAsync();
        var grantee = await _fixture.CreateUserAsync("alice");

        var dashboard = new JObject { ["name"] = "Grant listing" };
        DatabasePersistence.SaveEntity(owner, "Dashboards", dashboard);
        var dashId = dashboard["id"]!.ToString()!;

        DatabasePersistence.SaveResourceGrant("Dashboards", dashId, grantee, "edit", owner);

        var grants = DatabasePersistence.LoadResourceGrants("Dashboards", dashId);

        Assert.Single(grants);
        Assert.Equal("alice", grants[0]["username"]!.ToString());
        Assert.Equal("edit", grants[0]["permission"]!.ToString());
    }
}
