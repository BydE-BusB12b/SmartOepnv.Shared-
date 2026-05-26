using System.Text.Json.Nodes;



namespace SmartOepnv.Core.RoutePackage;



public static class RegisteredVehiclesEditor

{

    private const string PlannerMetaKey = "registeredVehiclesPlannerMeta";

    private const string PhoneRedirectsKey = "registeredVehiclesPlannerPhoneRedirects";



    public static IList<RegisteredVehiclePhoneRedirect> LoadPhoneRedirectsFromRoot(JsonObject root)

    {

        var list = new List<RegisteredVehiclePhoneRedirect>();

        if (root[PhoneRedirectsKey] is not JsonArray arr)

        {

            return list;

        }



        foreach (var node in arr.OfType<JsonObject>())

        {

            var from = node["fromPhoneNumber"]?.GetValue<string>() ?? string.Empty;

            var to = node["toPhoneNumber"]?.GetValue<string>() ?? string.Empty;

            if (NormalizePhoneKey(from).Length == 0 || NormalizePhoneKey(to).Length == 0)

            {

                continue;

            }



            list.Add(new RegisteredVehiclePhoneRedirect

            {

                FromPhoneNumber = from,

                ToPhoneNumber = to,

                RecordedAt = node["recordedAt"]?.GetValue<long>() ?? 0,

                Note = node["note"]?.GetValue<string>() ?? BuildRedirectNote(from, to)

            });

        }



        return list.OrderBy(r => r.RecordedAt).ToList();

    }



    public static void SavePhoneRedirectsToRoot(JsonObject root, IList<RegisteredVehiclePhoneRedirect> redirects)

    {

        if (redirects.Count == 0)

        {

            root.Remove(PhoneRedirectsKey);

            return;

        }



        var arr = new JsonArray();

        foreach (var r in redirects.OrderBy(x => x.RecordedAt))

        {

            arr.Add(new JsonObject

            {

                ["fromPhoneNumber"] = r.FromPhoneNumber,

                ["toPhoneNumber"] = r.ToPhoneNumber,

                ["recordedAt"] = r.RecordedAt,

                ["note"] = string.IsNullOrWhiteSpace(r.Note)

                    ? BuildRedirectNote(r.FromPhoneNumber, r.ToPhoneNumber)

                    : r.Note

            });

        }



        root[PhoneRedirectsKey] = arr;

    }



    public static IList<RegisteredVehicleItem> LoadFromRoot(JsonObject root)

    {

        var redirects = LoadPhoneRedirectsFromRoot(root);

        var metaByPhone = LoadPlannerMetaByPhone(root);

        var list = new List<RegisteredVehicleItem>();

        if (root["registeredVehicles"] is JsonArray arr)

        {

            foreach (var node in arr.OfType<JsonObject>())

            {

                list.Add(ParseKomVehicle(node));

            }

        }



        ApplyPhoneRedirects(list, metaByPhone, redirects);



        foreach (var item in list)

        {

            var key = NormalizePhoneKey(item.PhoneNumber);

            if (key.Length > 0 && metaByPhone.TryGetValue(key, out var details))

            {

                item.PlannerDetails = details.Clone();

            }



            item.LoadedPhoneNumber = item.PhoneNumber;

        }



        return list;

    }



    public static void SaveToRoot(

        JsonObject root,

        IList<RegisteredVehicleItem> vehicles,

        IList<RegisteredVehiclePhoneRedirect> phoneRedirects)

    {

        var arr = new JsonArray();

        foreach (var v in vehicles.Where(x => !string.IsNullOrWhiteSpace(x.PhoneNumber)))

        {

            arr.Add(WriteKomVehicle(v));

        }



        root["registeredVehicles"] = arr;

        root["registeredVehiclesMeta"] = new JsonObject

        {

            ["sentAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

            ["count"] = arr.Count

        };



        SavePlannerMeta(root, vehicles);

        SavePhoneRedirectsToRoot(root, phoneRedirects);

    }



    public static void Replace(EditableRoutePackage package, IList<RegisteredVehicleItem> vehicles)

    {

        package.RegisteredVehicles.Clear();

        foreach (var v in vehicles)

        {

            package.RegisteredVehicles.Add(v);

        }

    }



    public static void ReplacePhoneRedirects(

        EditableRoutePackage package,

        IList<RegisteredVehiclePhoneRedirect> redirects)

    {

        package.RegisteredVehiclePhoneRedirects.Clear();

        foreach (var r in redirects)

        {

            package.RegisteredVehiclePhoneRedirects.Add(r);

        }

    }



    /// <summary>

    /// Wendet dauerhafte Umleitungen an: Planer-Meta von alter Nummer übernehmen;

    /// Fahrzeug-Telefon nur anpassen, wenn noch die alte Nummer eingetragen ist.

    /// </summary>

    internal static void ApplyPhoneRedirects(

        IList<RegisteredVehicleItem> vehicles,

        Dictionary<string, RegisteredVehiclePlannerDetails> metaByPhone,

        IList<RegisteredVehiclePhoneRedirect> redirects)

    {

        foreach (var redirect in redirects.OrderBy(r => r.RecordedAt))

        {

            var fromKey = NormalizePhoneKey(redirect.FromPhoneNumber);

            var toKey = NormalizePhoneKey(redirect.ToPhoneNumber);

            if (fromKey.Length == 0 || toKey.Length == 0 || fromKey == toKey)

            {

                continue;

            }



            if (metaByPhone.TryGetValue(fromKey, out var metaFrom))

            {

                if (!metaByPhone.TryGetValue(toKey, out var metaTo))

                {

                    metaByPhone[toKey] = metaFrom.Clone();

                }

                else

                {

                    MergePlannerDetails(metaTo, metaFrom);

                }



                metaByPhone.Remove(fromKey);

            }



            foreach (var vehicle in vehicles)

            {

                var currentKey = NormalizePhoneKey(vehicle.PhoneNumber);

                if (currentKey == toKey)

                {

                    continue;

                }



                if (currentKey == fromKey)

                {

                    vehicle.PhoneNumber = redirect.ToPhoneNumber;

                }

            }

        }

    }



    private static RegisteredVehicleItem ParseKomVehicle(JsonObject obj) => new()

    {

        Name = obj["name"]?.GetValue<string>() ?? string.Empty,

        PhoneNumber = obj["phoneNumber"]?.GetValue<string>() ?? string.Empty,

        PersonnelNumber = obj["personnelNumber"]?.GetValue<string>() ?? string.Empty,

        Password = obj["password"]?.GetValue<string>() ?? string.Empty,

        LicenseExpiry = obj["licenseExpiry"]?.GetValue<string>() ?? string.Empty,

        FqnExpiry = obj["fqnExpiry"]?.GetValue<string>() ?? string.Empty,

        DriverCardExpiry = obj["driverCardExpiry"]?.GetValue<string>() ?? string.Empty,

        LoginAsMainDevice = obj["loginAsMainDevice"]?.GetValue<bool>() ?? false

    };



    private static JsonObject WriteKomVehicle(RegisteredVehicleItem v) => new()

    {

        ["name"] = v.Name,

        ["phoneNumber"] = v.PhoneNumber,

        ["personnelNumber"] = v.PersonnelNumber,

        ["password"] = v.Password,

        ["licenseExpiry"] = v.LicenseExpiry,

        ["fqnExpiry"] = v.FqnExpiry,

        ["driverCardExpiry"] = v.DriverCardExpiry,

        ["loginAsMainDevice"] = v.LoginAsMainDevice

    };



    private static Dictionary<string, RegisteredVehiclePlannerDetails> LoadPlannerMetaByPhone(JsonObject root)

    {

        var map = new Dictionary<string, RegisteredVehiclePlannerDetails>(StringComparer.Ordinal);

        if (root[PlannerMetaKey] is not JsonArray arr)

        {

            return map;

        }



        foreach (var node in arr.OfType<JsonObject>())

        {

            var phone = node["phoneNumber"]?.GetValue<string>() ?? string.Empty;

            var key = NormalizePhoneKey(phone);

            if (key.Length == 0)

            {

                continue;

            }



            map[key] = ParsePlannerMeta(node);

        }



        return map;

    }



    private static void SavePlannerMeta(JsonObject root, IList<RegisteredVehicleItem> vehicles)

    {

        var arr = new JsonArray();

        foreach (var v in vehicles)

        {

            var phone = v.PhoneNumber.Trim();

            if (phone.Length == 0)

            {

                continue;

            }



            if (!v.PlannerDetails.HasAnyValue())

            {

                continue;

            }



            arr.Add(WritePlannerMeta(phone, v.PlannerDetails));

        }



        if (arr.Count == 0)

        {

            root.Remove(PlannerMetaKey);

            return;

        }



        root[PlannerMetaKey] = arr;

    }



    private static RegisteredVehiclePlannerDetails ParsePlannerMeta(JsonObject obj) => new()

    {

        VehicleType = obj["vehicleType"]?.GetValue<string>() ?? string.Empty,

        Vin = obj["vin"]?.GetValue<string>() ?? string.Empty,

        SeatBelts = obj["seatBelts"]?.GetValue<string>() ?? string.Empty,

        Climate = obj["climate"]?.GetValue<string>() ?? string.Empty,

        PermittedTotalMassKg = obj["permittedTotalMassKg"]?.GetValue<string>() ?? string.Empty,

        EmptyWeightKg = obj["emptyWeightKg"]?.GetValue<string>() ?? string.Empty,

        NextMainInspection = obj["nextMainInspection"]?.GetValue<string>() ?? string.Empty,

        NextSpInspection = obj["nextSpInspection"]?.GetValue<string>() ?? string.Empty

    };



    private static JsonObject WritePlannerMeta(string phoneNumber, RegisteredVehiclePlannerDetails d) =>

        new()

        {

            ["phoneNumber"] = phoneNumber,

            ["vehicleType"] = d.VehicleType,

            ["vin"] = d.Vin,

            ["seatBelts"] = d.SeatBelts,

            ["climate"] = d.Climate,

            ["permittedTotalMassKg"] = d.PermittedTotalMassKg,

            ["emptyWeightKg"] = d.EmptyWeightKg,

            ["nextMainInspection"] = d.NextMainInspection,

            ["nextSpInspection"] = d.NextSpInspection

        };



    private static void MergePlannerDetails(

        RegisteredVehiclePlannerDetails target,

        RegisteredVehiclePlannerDetails source)

    {

        if (string.IsNullOrWhiteSpace(target.VehicleType))

        {

            target.VehicleType = source.VehicleType;

        }



        if (string.IsNullOrWhiteSpace(target.Vin))

        {

            target.Vin = source.Vin;

        }



        if (string.IsNullOrWhiteSpace(target.SeatBelts))

        {

            target.SeatBelts = source.SeatBelts;

        }



        if (string.IsNullOrWhiteSpace(target.Climate))

        {

            target.Climate = source.Climate;

        }



        if (string.IsNullOrWhiteSpace(target.PermittedTotalMassKg))

        {

            target.PermittedTotalMassKg = source.PermittedTotalMassKg;

        }



        if (string.IsNullOrWhiteSpace(target.EmptyWeightKg))

        {

            target.EmptyWeightKg = source.EmptyWeightKg;

        }



        if (string.IsNullOrWhiteSpace(target.NextMainInspection))

        {

            target.NextMainInspection = source.NextMainInspection;

        }



        if (string.IsNullOrWhiteSpace(target.NextSpInspection))

        {

            target.NextSpInspection = source.NextSpInspection;

        }

    }



    public static string NormalizePhoneKey(string? raw)

    {

        if (string.IsNullOrWhiteSpace(raw))

        {

            return string.Empty;

        }



        return new string(raw.Where(char.IsDigit).ToArray());

    }



    public static string BuildRedirectNote(string fromPhone, string toPhone) =>

        $"Nr. {fromPhone.Trim()} ist nun Nr. {toPhone.Trim()}";



    public static bool TryAppendPhoneRedirect(

        IList<RegisteredVehiclePhoneRedirect> redirects,

        string fromPhone,

        string toPhone)

    {

        var fromKey = NormalizePhoneKey(fromPhone);

        var toKey = NormalizePhoneKey(toPhone);

        if (fromKey.Length == 0 || toKey.Length == 0 || fromKey == toKey)

        {

            return false;

        }



        if (redirects.Any(r =>

                NormalizePhoneKey(r.FromPhoneNumber) == fromKey &&

                NormalizePhoneKey(r.ToPhoneNumber) == toKey))

        {

            return false;

        }



        redirects.Add(new RegisteredVehiclePhoneRedirect

        {

            FromPhoneNumber = fromPhone.Trim(),

            ToPhoneNumber = toPhone.Trim(),

            RecordedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

            Note = BuildRedirectNote(fromPhone, toPhone)

        });

        return true;

    }

}



internal static class RegisteredVehiclePlannerDetailsExtensions

{

    public static bool HasAnyValue(this RegisteredVehiclePlannerDetails d) =>

        !string.IsNullOrWhiteSpace(d.VehicleType) ||

        !string.IsNullOrWhiteSpace(d.Vin) ||

        !string.IsNullOrWhiteSpace(d.SeatBelts) ||

        !string.IsNullOrWhiteSpace(d.Climate) ||

        !string.IsNullOrWhiteSpace(d.PermittedTotalMassKg) ||

        !string.IsNullOrWhiteSpace(d.EmptyWeightKg) ||

        !string.IsNullOrWhiteSpace(d.NextMainInspection) ||

        !string.IsNullOrWhiteSpace(d.NextSpInspection);

}


