using Newtonsoft.Json.Linq;
using Server.Core;

namespace Server.Tests;

[Collection("Database")]
public class EntityPersistenceTests
{
    private readonly DatabaseFixture _fixture;

    public EntityPersistenceTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SaveEntity_then_LoadEntities_returns_the_saved_row()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        var dashboard = new JObject { ["name"] = "My Dashboard", ["components"] = new JArray() };
        DatabasePersistence.SaveEntity(userId, "Dashboards", dashboard);

        var loaded = DatabasePersistence.LoadEntities(userId, "Dashboards");

        Assert.Single(loaded);
        Assert.Equal(dashboard["id"]!.ToString(), loaded[0]["id"]!.ToString());
    }

    [Fact]
    public async Task DeleteEntity_removes_the_row()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        var dashboard = new JObject { ["name"] = "To Delete" };
        DatabasePersistence.SaveEntity(userId, "Dashboards", dashboard);
        DatabasePersistence.DeleteEntity(userId, "Dashboards", dashboard["id"]!.ToString()!);

        Assert.Empty(DatabasePersistence.LoadEntities(userId, "Dashboards"));
    }

    // Regression test for the 42P08 "could not determine data type of parameter" bug: calling
    // LoadEntities with no projectId used to bind @ProjectId as a typeless null (DBNull.Value
    // boxed as object), which Npgsql can't assign a data type to. This is exactly how
    // LoadDashboards/LoadReports call it (Reports.vue's "View all" page loaded empty because of
    // it), so it must succeed rather than throwing.
    [Fact]
    public async Task LoadEntities_without_a_projectId_does_not_throw_and_returns_only_the_users_rows()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();
        var otherUserId = await _fixture.CreateUserAsync();

        DatabasePersistence.SaveEntity(userId, "Dashboards", new JObject { ["name"] = "Mine" });
        DatabasePersistence.SaveEntity(otherUserId, "Dashboards", new JObject { ["name"] = "Not mine" });

        var loaded = DatabasePersistence.LoadEntities(userId, "Dashboards");

        Assert.Single(loaded);
        Assert.Equal("Mine", loaded[0]["name"]!.ToString());
    }

    [Fact]
    public async Task LoadEntities_with_a_projectId_only_returns_rows_in_that_project()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        var project = new JObject { ["name"] = "Project A" };
        DatabasePersistence.SaveProject(userId, project);
        var projectId = project["id"]!.ToString();

        DatabasePersistence.SaveEntity(userId, "Dashboards", new JObject { ["name"] = "In project", ["projectId"] = projectId });
        DatabasePersistence.SaveEntity(userId, "Dashboards", new JObject { ["name"] = "No project" });

        var loaded = DatabasePersistence.LoadEntities(userId, "Dashboards", projectId);

        Assert.Single(loaded);
        Assert.Equal("In project", loaded[0]["name"]!.ToString());
    }

    [Fact]
    public async Task GenerateShareToken_then_LoadEntityByShareToken_finds_the_row_and_RevokeShareToken_clears_it()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        var dashboard = new JObject { ["name"] = "Shared" };
        DatabasePersistence.SaveEntity(userId, "Dashboards", dashboard);
        var id = dashboard["id"]!.ToString()!;

        var token = DatabasePersistence.GenerateShareToken(userId, "Dashboards", id);
        var found = DatabasePersistence.LoadEntityByShareToken("Dashboards", token!);
        Assert.NotNull(found);
        Assert.Equal(id, found!["id"]!.ToString());

        DatabasePersistence.RevokeShareToken(userId, "Dashboards", id);
        Assert.Null(DatabasePersistence.LoadEntityByShareToken("Dashboards", token!));
    }
}
