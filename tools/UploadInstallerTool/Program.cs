using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.Updates;

if (args.Length < 3)
{
    Console.Error.WriteLine("Verwendung: UploadInstallerTool <SetupExePfad> <Planer|Leitstelle> <Version>");
    return 1;
}

var setupExePath = Path.GetFullPath(args[0]);
var product = args[1].Trim();
var version = args[2].Trim();

if (!string.Equals(product, "Planer", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(product, "Leitstelle", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Produkt muss Planer oder Leitstelle sein.");
    return 1;
}

var isLeitstelle = string.Equals(product, "Leitstelle", StringComparison.OrdinalIgnoreCase);
var settingsSubfolder = isLeitstelle ? "Leitstelle" : "Planer";

try
{
    AppServices.Initialize(settingsSubfolder);
    var sizeMb = Math.Round(new FileInfo(setupExePath).Length / 1024d / 1024d, 1);
    Console.WriteLine($"Dropbox-Upload: {setupExePath} ({sizeMb} MB)");

    var result = await DesktopInstallerDropboxPublisher.PublishAsync(setupExePath, isLeitstelle, version)
        .ConfigureAwait(false);

    Console.WriteLine($"Setup hochgeladen: {result.SetupDropboxPath}");
    Console.WriteLine($"{DropboxConstants.SoftwareVersionsFileName} aktualisiert ({product} -> {result.Version}).");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Dropbox-Upload fehlgeschlagen: {ex.Message}");
    return 1;
}
