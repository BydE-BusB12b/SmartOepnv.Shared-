using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;
using Xunit;

namespace SmartOepnv.Core.Tests;

public class AnnouncementVoiceFilesTests
{
    [Fact]
    public void Voice2_name_inserts_suffix_before_extension()
    {
        Assert.Equal("und_2.wav", AnnouncementVoiceFiles.Voice2FileName("und.wav"));
        Assert.Equal("Nach_2.wav", AnnouncementVoiceFiles.Voice2FileName("Nach.wav"));
        Assert.Equal("Dieser Bus fährt über_2.wav", AnnouncementVoiceFiles.Voice2FileName("Dieser Bus fährt über.wav"));
        Assert.Equal("RE47X_2.wav", AnnouncementVoiceFiles.Voice2FileName("RE47X.wav"));
        Assert.Equal("0193_Begrüßung 1_2.wav", AnnouncementVoiceFiles.Voice2FileName("0193_Begrüßung 1.wav"));
    }

    [Fact]
    public void Voice2_name_is_not_doubled()
    {
        Assert.Equal("und_2.wav", AnnouncementVoiceFiles.Voice2FileName("und_2.wav"));
    }

    [Fact]
    public void Export_names_keep_voice2_siblings_of_spoken_files()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0193_Begrüßung 1.wav",
            "0084_Kaarster See.wav",
            "Nach.wav",
            "Hambloch Gong.wav",
            "Next Stop.wav"
        };

        AnnouncementVoiceFiles.IncludeExistingVoice2Siblings(
            names,
            voice2Name => voice2Name is "0193_Begrüßung 1_2.wav"
                or "0084_Kaarster See_2.wav"
                or "Nach_2.wav"
                or "Hambloch Gong_2.wav"
                or "Next Stop_2.wav");

        Assert.Contains("0193_Begrüßung 1_2.wav", names);
        Assert.Contains("0084_Kaarster See_2.wav", names);
        Assert.Contains("Nach_2.wav", names);
        Assert.DoesNotContain("Hambloch Gong_2.wav", names);
        Assert.DoesNotContain("Next Stop_2.wav", names);
    }

    [Fact]
    public void Route_voice_map_stores_only_voice_2()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        const string route = "Düsseldorf (Linie: RE47, Fahrt: 1)";
        RouteAnnouncementVoiceEditor.SetForRoute(map, route, AnnouncementVoiceFiles.Voice2);
        Assert.Equal(AnnouncementVoiceFiles.Voice2, RouteAnnouncementVoiceEditor.GetForRoute(map, route));

        RouteAnnouncementVoiceEditor.SetForRoute(map, route, AnnouncementVoiceFiles.PetraLabel);
        Assert.Equal(string.Empty, RouteAnnouncementVoiceEditor.GetForRoute(map, route));

        RouteAnnouncementVoiceEditor.SetForRoute(map, route, "2");
        var root = new JsonObject();
        RouteAnnouncementVoiceEditor.SaveToRoot(root, [route], map);
        var loaded = RouteAnnouncementVoiceEditor.LoadFromRoot(root);
        Assert.Equal("2", RouteAnnouncementVoiceEditor.GetForRoute(loaded, route));
    }
}
