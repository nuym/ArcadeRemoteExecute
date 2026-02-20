namespace ArcadeRemoteExecute.Server;

public class UpdateManifest
{
    public List<UpdateEntry> Files { get; set; } = new();
}

public class UpdateEntry
{
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
}
