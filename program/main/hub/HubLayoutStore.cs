using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace MainApp;
public class NodePos { public string Path { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public string? PointId { get; set; } }
public class ZoneData { public string Color = ""; public string Id { get; set; } = ""; public string Name { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public double W { get; set; } public double H { get; set; } }
public class PointData { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public double X { get; set; } public double Y { get; set; } }
public class HubLayout
{
public double PanX { get; set; } public double PanY { get; set; } public double Zoom { get; set; } = 1; public int Ver { get; set; }
public double HubX { get; set; } public double HubY { get; set; }
public List<NodePos> Nodes { get; set; } = new();
public List<ZoneData> Zones { get; set; } = new();
public List<PointData> Points { get; set; } = new();
}
public static class HubLayoutStore
{
private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
public static HubLayout Load()
{
try
{
var path = BrowserLauncher.GetConfigPath();
if (path == null || !File.Exists(path)) return new HubLayout();
var node = JsonNode.Parse(File.ReadAllText(path));
var l = node?["HubLayout"];
if (l == null) return new HubLayout();
return JsonSerializer.Deserialize<HubLayout>(l.ToJsonString(), Opts) ?? new HubLayout();
}
catch { return new HubLayout(); }
}
public static void Save(HubLayout layout)
{
try
{
var path = BrowserLauncher.GetConfigPath();
if (path == null) return;
JsonObject node;
try { node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject(); }
catch { node = new JsonObject(); }
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
node["HubLayout"] = JsonSerializer.SerializeToNode(layout);
File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}
catch { }
}
}