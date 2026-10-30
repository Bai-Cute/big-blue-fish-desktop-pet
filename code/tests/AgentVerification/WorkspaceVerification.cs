using System.Text.Json;
using BigBlueFish.Agent;

static class WorkspaceVerification
{
    public static void Run(string root, Action<bool, string> check)
    {
        string data = Path.Combine(root, "control"), first = Path.Combine(root, "files");
        var store = new AgentWorkspaceStore(data, first); store.EnsureCurrent(); store.Open(first);
        check(new AgentWorkspaceStore(data, Path.Combine(root, "ignored")).Current == first, "current workspace survives host reconstruction");
        var claude = new AgentSkillStore(data, HarnessCatalog.Get("claude"), store.Current);
        var codex = new AgentSkillStore(data, HarnessCatalog.Get("codex"), store.Current); claude.Install(); codex.Install();
        check(claude.Workspace == codex.Workspace && claude.DirectoryPath != codex.DirectoryPath, "harnesses share user files with native skill discovery directories");
        check(store.SessionPath("claude") != store.SessionPath("codex"), "conversation bindings isolated by harness");
        string firstBinding = store.SessionPath("codex"); store.Create(root, "另一个工作区");
        check(store.Current != first && store.SessionPath("codex") != firstBinding, "new workspace creates independent conversation namespace");
        File.WriteAllText(Path.Combine(store.Current, "项目.txt"), "user work"); store.Open(first);
        check(store.SessionPath("codex") == firstBinding, "reopening workspace restores same conversation namespace");
        AtomicText.Write(firstBinding, "{}"); File.WriteAllText(Path.Combine(first, "report.txt"), "report");
        string outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "model.gguf"), "not workspace data");
        bool linked = false;
        try { Directory.CreateSymbolicLink(Path.Combine(first, "outside-link"), outside); linked = true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
        store.Clear(first);
        check(Directory.Exists(first) && !Directory.EnumerateFileSystemEntries(first).Any(), "clear keeps workspace directory and removes its contents");
        check(!File.Exists(firstBinding) && File.Exists(Path.Combine(outside, "model.gguf")), "clear invalidates conversations and preserves files outside workspace" + (linked ? " including directory-link targets" : ""));
        bool rejected = false; try { store.ValidateClear(outside); } catch (IOException) { rejected = true; }
        check(rejected, "clear requires exact currently displayed workspace");
        store.Open(data); rejected = false; try { store.ValidateClear(data); } catch (IOException) { rejected = true; }
        check(rejected, "application control data cannot be cleared as workspace"); store.Open(first);
        var config = new AgentSessionConfiguration();
        config.Read(JsonSerializer.SerializeToElement(new { configOptions = new object[] {
            new {id="model",name="Model",category="model",type="select",currentValue="fast",options=new[]{new {group="provider",name="Provider",options=new[]{new {value="fast",name="Fast"}}}}},
            new {id="permission",name="Permission",type="boolean",currentValue=false},
            new {id="future",name="Future",type="future-widget",currentValue="x"}
        }}));
        check(config.Options.Length == 2 && config.Options[0].Choices[0].Group == "Provider", "grouped model and boolean config parsed; unknown widget ignored");
        config.Read(JsonSerializer.SerializeToElement(new { modes=new{currentModeId="fake",availableModes=new[]{new{id="fake",name="Fake"}}}}));
        check(config.Options.Length == 2, "modern config takes precedence over legacy modes");
        config.Notification(JsonSerializer.SerializeToElement(new {sessionUpdate="config_option_update",configOptions=Array.Empty<object>()}));
        check(config.Options.Length == 0, "configuration notification replaces entire list including removal");
        config.Reset(); config.Read(JsonSerializer.SerializeToElement(new {models=new {currentModelId="native", availableModels=new[]{new{modelId="native",name="Native"}}},modes=new{currentModeId="ask",availableModes=new[]{new{id="ask",name="Ask"}}}}));
        check(config.Options.Length == 2 && config.Options[0].Method == "session/set_model", "legacy model and mode selectors use native compatibility methods");
        check(new AcpException(JsonSerializer.SerializeToElement(new {code=-32002,message="Resource not found"})).SessionMissing, "missing saved session defaults to fresh session");
        check(!new AcpException(JsonSerializer.SerializeToElement(new {code=-32000,message="Session authentication expired"})).SessionMissing, "authentication error does not discard saved conversation");
    }
}
