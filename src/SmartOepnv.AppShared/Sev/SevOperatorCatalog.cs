using SmartOepnv.Core.Sev;



namespace SmartOepnv.AppShared.Sev;



public sealed class SevOperatorOption

{

    public required SevOperatorKind Kind { get; init; }



    public required string DisplayName { get; init; }



    public required string PreviewAssetFileName { get; init; }



    public required IReadOnlyList<string> PdfLogoAssetFileNames { get; init; }



    public float OperatorLogoLeftMm { get; init; } = 6.9f;



    public float OperatorLogoTopMm { get; init; } = 239.8f;



    public float OperatorLogoWidthMm { get; init; } = 109.4f;



    public float OperatorLogoHeightMm { get; init; } = 57.2f;



    /// <summary>Großes DB/Eurobahn-Layout links; sonst Logo in der Footer-Zeile.</summary>

    public bool UseLargeOperatorLogo { get; init; } = true;



    /// <summary>Reihenfolge in der Footer-Zeile (kleiner = weiter links).</summary>

    public int FooterSortOrder { get; init; } = 50;



    /// <summary>RegioBahn wird in der Zeile zentriert; weitere Logos links/rechts davon.</summary>

    public bool CenterInFooterRow { get; init; }



    /// <summary>Logo erscheint in der Zeile „Im Auftrag von“ (nicht z. B. nur mobil.nrw vor QR).</summary>

    public bool IncludeInFooterLogoRow { get; init; } = true;



}



public static class SevOperatorCatalog

{

    public static IReadOnlyList<SevOperatorOption> All { get; } =

    [

        new()

        {

            Kind = SevOperatorKind.RheinRuhrBahn,

            DisplayName = "RheinRuhrBahn",

            PreviewAssetFileName = "operator_rheinruhrbahn.png",

            PdfLogoAssetFileNames = ["operator_rheinruhrbahn.png"],

            OperatorLogoWidthMm = 38.3f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 30

        },

        new()

        {

            Kind = SevOperatorKind.RegioBahn,

            DisplayName = "RegioBahn",

            PreviewAssetFileName = "operator_regiobahn_475x106.png",

            PdfLogoAssetFileNames = ["operator_regiobahn_475x106.png"],

            OperatorLogoWidthMm = 72f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 100,

            CenterInFooterRow = true

        },

        new()

        {

            Kind = SevOperatorKind.DeutscheBahn,

            DisplayName = "Deutsche Bahn",

            PreviewAssetFileName = "operator_deutsche_bahn.png",

            PdfLogoAssetFileNames = ["operator_deutsche_bahn.png", "operator_deutsche_bahn_310x163.png"],

            OperatorLogoWidthMm = 109.4f,

            OperatorLogoHeightMm = 30.2f

        },

        new()

        {

            Kind = SevOperatorKind.Eurobahn,

            DisplayName = "Eurobahn",

            PreviewAssetFileName = "operator_re13_eurobahn.png",

            PdfLogoAssetFileNames = ["operator_re13_eurobahn.png"],

            OperatorLogoWidthMm = 68f,

            OperatorLogoHeightMm = 34f

        },

        new()

        {

            Kind = SevOperatorKind.NationalExpress,

            DisplayName = "National Express",

            PreviewAssetFileName = "operator_national_express.png",

            PdfLogoAssetFileNames = ["operator_national_express.png"],

            OperatorLogoWidthMm = 48f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 40

        },

        new()

        {

            Kind = SevOperatorKind.GoRheinland,

            DisplayName = "GO Rheinland",

            PreviewAssetFileName = "operator_go_rheinland.png",

            PdfLogoAssetFileNames = ["operator_go_rheinland.png"],

            OperatorLogoWidthMm = 58f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 150

        },

        new()

        {

            Kind = SevOperatorKind.Westfalenbahn,

            DisplayName = "WestfalenBahn",

            PreviewAssetFileName = "operator_westfalenbahn.png",

            PdfLogoAssetFileNames = ["operator_westfalenbahn.png"],

            OperatorLogoWidthMm = 73f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 42

        },

        new()

        {

            Kind = SevOperatorKind.Vias,

            DisplayName = "VIAS",

            PreviewAssetFileName = "operator_vias.png",

            PdfLogoAssetFileNames = ["operator_vias.png"],

            OperatorLogoWidthMm = 40f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 45

        },

        new()

        {

            Kind = SevOperatorKind.SBahnRheinRuhr,

            DisplayName = "S-Bahn Rhein-Ruhr",

            PreviewAssetFileName = "operator_sbahn_rheinruhr.jpeg",

            PdfLogoAssetFileNames = ["operator_sbahn_rheinruhr.jpeg"],

            OperatorLogoWidthMm = 77f,

            OperatorLogoHeightMm = 15f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 35

        },

        new()

        {

            Kind = SevOperatorKind.Vrr,

            DisplayName = "VRR",

            PreviewAssetFileName = "footer_vrr.png",

            PdfLogoAssetFileNames = ["footer_vrr.png"],

            OperatorLogoWidthMm = 40.8f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 200

        },

        new()

        {

            Kind = SevOperatorKind.MobilNrw,

            DisplayName = "mobil.nrw",

            PreviewAssetFileName = "mobil_nrw_logo.png",

            PdfLogoAssetFileNames = ["mobil_nrw_logo.png"],

            OperatorLogoWidthMm = 37.6f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 190

        },

        new()

        {

            Kind = SevOperatorKind.Arriva,

            DisplayName = "Arriva",

            PreviewAssetFileName = "operator_arriva.png",

            PdfLogoAssetFileNames = ["operator_arriva.png"],

            OperatorLogoWidthMm = 42f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 43

        },

        new()

        {

            Kind = SevOperatorKind.Ns,

            DisplayName = "NS",

            PreviewAssetFileName = "operator_ns.png",

            PdfLogoAssetFileNames = ["operator_ns.png"],

            OperatorLogoWidthMm = 78f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 44

        },

        new()

        {

            Kind = SevOperatorKind.Nwl,

            DisplayName = "NWL",

            PreviewAssetFileName = "operator_nwl.png",

            PdfLogoAssetFileNames = ["operator_nwl.png"],

            OperatorLogoWidthMm = 48f,

            OperatorLogoHeightMm = 30.6f,

            UseLargeOperatorLogo = false,

            FooterSortOrder = 195

        }

    ];



    public static SevOperatorOption Get(SevOperatorKind kind) =>

        All.First(o => o.Kind == kind);



    public static IReadOnlyList<SevOperatorOption> GetMany(IEnumerable<SevOperatorKind> kinds)

    {

        var selected = new List<SevOperatorOption>();

        var seen = new HashSet<SevOperatorKind>();



        foreach (var kind in kinds)

        {

            if (seen.Add(kind))

            {

                selected.Add(Get(kind));

            }

        }



        return selected.Count > 0 ? selected : [Get(SevOperatorKind.RegioBahn)];

    }

}


