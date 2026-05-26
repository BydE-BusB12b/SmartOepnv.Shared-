using System.Text.Json;
var path = @"C:\Users\hkx18\AndroidStudioProjects\SmartOepnv.Planer\routes_export.json";
await using var fs = File.OpenRead(path);
using var doc = await JsonDocument.ParseAsync(fs, new JsonDocumentOptions { AllowTrailingCommas = true });
foreach (var p in doc.RootElement.EnumerateObject()) {
  var kind = p.Value.ValueKind;
  var extra = kind switch {
    JsonValueKind.Array => $"[{p.Value.GetArrayLength()}]",
    JsonValueKind.Object => $"{{{p.Value.EnumerateObject().Count()}}}",
    _ => ""
  };
  Console.WriteLine(p.Name + " " + kind + " " + extra);
}
