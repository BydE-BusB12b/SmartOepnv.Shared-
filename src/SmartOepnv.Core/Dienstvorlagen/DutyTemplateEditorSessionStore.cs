using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Dienstvorlagen;

public sealed class DutyTemplateEditorSessionStore
{
    public const string SessionFileName = "duty_template_editor_session.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _sessionPath;

    public DutyTemplateEditorSessionStore(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _sessionPath = Path.Combine(workspaceDir, SessionFileName);
    }

    public string SessionFilePath => _sessionPath;

    public DutyTemplateEditorSession? Load()
    {
        if (!File.Exists(_sessionPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_sessionPath);
            return JsonSerializer.Deserialize<DutyTemplateEditorSession>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(DutyTemplateEditorSession session)
    {
        session.SavedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var envelope = new DutyTemplateEditorSessionEnvelope
        {
            Version = DutyTemplateEditorSession.FileVersion,
            Session = session
        };
        SafeDataFileStore.WriteAllText(_sessionPath, JsonSerializer.Serialize(envelope, JsonOptions));
    }

    public void Clear()
    {
        if (File.Exists(_sessionPath))
        {
            File.Delete(_sessionPath);
        }
    }

    private sealed class DutyTemplateEditorSessionEnvelope
    {
        public int Version { get; set; } = DutyTemplateEditorSession.FileVersion;

        public DutyTemplateEditorSession Session { get; set; } = new();
    }
}
