using Newtonsoft.Json.Linq;
using Server.Core;

namespace Server.Tests;

[Collection("Database")]
public class ScriptsTests
{
    private readonly DatabaseFixture _fixture;

    public ScriptsTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SaveScript_then_LoadScripts_returns_the_saved_row()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        var script = new JObject { ["name"] = "My Query", ["code"] = "SELECT 1" };
        DatabasePersistence.SaveScript(userId, script, "sql");

        var loaded = DatabasePersistence.LoadScripts(userId, "sql");

        Assert.Single(loaded);
        Assert.Equal(script["id"]!.ToString(), loaded[0]["id"]!.ToString());
    }

    // Same regression as EntityPersistenceTests' LoadEntities test, but for the sibling
    // LoadScripts method, which had the identical @ProjectId-bound-as-DBNull.Value bug.
    [Fact]
    public async Task LoadScripts_without_a_projectId_does_not_throw()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        DatabasePersistence.SaveScript(userId, new JObject { ["name"] = "Q1", ["code"] = "SELECT 1" }, "sql");

        var loaded = DatabasePersistence.LoadScripts(userId, "sql");

        Assert.Single(loaded);
    }

    [Fact]
    public async Task LoadScripts_with_a_projectId_only_returns_scripts_in_that_project()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        var project = new JObject { ["name"] = "Script Project" };
        DatabasePersistence.SaveProject(userId, project);
        var projectId = project["id"]!.ToString();

        DatabasePersistence.SaveScript(userId, new JObject { ["name"] = "In project", ["code"] = "SELECT 1", ["projectId"] = projectId }, "sql");
        DatabasePersistence.SaveScript(userId, new JObject { ["name"] = "No project", ["code"] = "SELECT 2" }, "sql");

        var loaded = DatabasePersistence.LoadScripts(userId, "sql", projectId);

        Assert.Single(loaded);
        Assert.Equal("In project", loaded[0]["name"]!.ToString());
    }

    [Fact]
    public async Task DeleteScript_removes_the_row()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        var script = new JObject { ["name"] = "To delete", ["code"] = "SELECT 1" };
        DatabasePersistence.SaveScript(userId, script, "sql");
        DatabasePersistence.DeleteScript(userId, script["id"]!.ToString()!, "sql");

        Assert.Empty(DatabasePersistence.LoadScripts(userId, "sql"));
    }

    [Fact]
    public async Task UpdateScriptSchedule_does_not_clobber_the_scripts_code()
    {
        if (_fixture.SkipReason != null) return;
        var userId = await _fixture.CreateUserAsync();

        var script = new JObject { ["name"] = "Scheduled", ["code"] = "SELECT 42" };
        DatabasePersistence.SaveScript(userId, script, "sql");
        var id = script["id"]!.ToString()!;

        DatabasePersistence.UpdateScriptSchedule(userId, id, "{\"cron\":\"0 * * * *\"}");

        var loaded = DatabasePersistence.LoadScripts(userId, "sql");
        Assert.Single(loaded);
        Assert.Equal("SELECT 42", loaded[0]["code"]!.ToString());
        Assert.Contains("cron", loaded[0]["schedule"]!.ToString());
    }
}
