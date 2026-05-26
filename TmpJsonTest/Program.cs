using System.Text.Json;
using System.Text.Json.Nodes;

var drafts = new JsonObject();
drafts["Route A"] = "{\"roadSnappedEdgeKeys\":[\"a\"]}";
var root = new JsonObject { ["routePathDrafts"] = drafts };
try {
  root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
  Console.WriteLine("draft string value + options: OK");
} catch (Exception ex) {
  Console.WriteLine("draft string value + options: FAIL - " + ex.Message);
}

var drafts2 = new JsonObject();
drafts2["Route A"] = JsonNode.Parse("{\"roadSnappedEdgeKeys\":[\"a\"]}");
try {
  root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
} catch (Exception ex) {
  Console.WriteLine("draft object value: " + ex.Message);
}
