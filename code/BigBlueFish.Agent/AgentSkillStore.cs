using System.Reflection;
using System.Text;

namespace BigBlueFish.Agent;

public sealed class AgentSkillStore
{
    public string Workspace { get; }
    public string DirectoryPath { get; }
    public string PersonaPath => Path.Combine(DirectoryPath, "persona.md");
    public string SkillPath => Path.Combine(DirectoryPath, "SKILL.md");
    public AgentSkillStore(string root, HarnessDefinition harness, string? workspace = null)
    {
        Workspace = Path.GetFullPath(workspace ?? Path.Combine(root, "workspaces", harness.Id));
        DirectoryPath = Path.Combine(Workspace, harness.SkillDirectory.Replace('/', Path.DirectorySeparatorChar), "big-blue-fish");
    }
    public void Install()
    {
        Directory.CreateDirectory(DirectoryPath);
        // Skills are installed inside the pet-owned workspace, never the user's global harness configuration.
        string receipt = Path.Combine(DirectoryPath, ".fish-skill.sha256");
        string? currentHash = File.Exists(SkillPath) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(SkillPath))) : null;
        bool managed = currentHash == null || currentHash == "C48D05F6D922B1C7EF68C5E991E6E790484FA5916D8C85A429E0599A17CF03DF"
            || File.Exists(receipt) && File.ReadAllText(receipt) == currentHash;
        if (managed)
        {
            string content = Resource("SKILL.md");
            if (!File.Exists(SkillPath) || File.ReadAllText(SkillPath) != content) AtomicText.Write(SkillPath, content);
            AtomicText.Write(receipt, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(SkillPath))));
        }
        if (!File.Exists(PersonaPath)) AtomicText.Write(PersonaPath, Resource("persona.md"));
    }
    public string Guide() => File.ReadAllText(SkillPath) + "\n\n" + File.ReadAllText(PersonaPath);
    private static string Resource(string name)
    {
        using var stream = typeof(AgentSkillStore).Assembly.GetManifestResourceStream("BigBlueFish.Agent.Skills." + name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8); return reader.ReadToEnd();
    }
}

public static class AtomicText
{
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
