using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

public static class TicketProductEditor
{
    public const string RootKey = "ticketProducts";

    public static void LoadFromRoot(JsonObject root, IList<TicketProductItem> target)
    {
        target.Clear();
        if (root[RootKey] is JsonArray arr)
        {
            var i = 0;
            foreach (var node in arr)
            {
                if (node is not JsonObject o) continue;
                var name = o["name"]?.GetValue<string>()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;
                var priceCents = o["priceCents"]?.GetValue<int>()
                    ?? (int)Math.Round((o["priceEuro"]?.GetValue<decimal>() ?? 0m) * 100m);
                target.Add(new TicketProductItem
                {
                    Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    Name = name,
                    PriceCents = Math.Max(0, priceCents),
                    IsDefault = o["isDefault"]?.GetValue<bool>() == true || o["default"]?.GetValue<bool>() == true,
                    Active = o["active"]?.GetValue<bool>() != false,
                    SortOrder = o["sortOrder"]?.GetValue<int>() ?? i
                });
                i++;
            }
        }

        if (target.Count == 0)
        {
            foreach (var p in CreateDefaultProducts())
            {
                target.Add(p);
            }
        }

        EnsureSingleDefault(target);
    }

    /// <summary>Gleicher Katalog wie Android <c>MuseumTicketing</c>-Seed.</summary>
    public static IReadOnlyList<TicketProductItem> CreateDefaultProducts() =>
    [
        new TicketProductItem
        {
            Id = "seed_adult",
            Name = "Erwachsener",
            PriceCents = 800,
            IsDefault = true,
            Active = true,
            SortOrder = 0
        },
        new TicketProductItem
        {
            Id = "seed_child",
            Name = "Kind (6–14)",
            PriceCents = 400,
            IsDefault = false,
            Active = true,
            SortOrder = 1
        },
        new TicketProductItem
        {
            Id = "seed_family",
            Name = "Familie",
            PriceCents = 2000,
            IsDefault = false,
            Active = true,
            SortOrder = 2
        },
        new TicketProductItem
        {
            Id = "seed_kombi",
            Name = "Kombi Museum+Fahrt",
            PriceCents = 1200,
            IsDefault = false,
            Active = true,
            SortOrder = 3
        }
    ];

    public static void SaveToRoot(JsonObject root, IList<TicketProductItem> products)
    {
        EnsureSingleDefault(products);
        var arr = new JsonArray();
        foreach (var p in products.OrderBy(x => x.SortOrder).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            arr.Add(new JsonObject
            {
                ["id"] = p.Id,
                ["name"] = p.Name,
                ["priceCents"] = p.PriceCents,
                ["isDefault"] = p.IsDefault,
                ["active"] = p.Active,
                ["sortOrder"] = p.SortOrder
            });
        }
        root[RootKey] = arr;
    }

    public static void EnsureSingleDefault(IList<TicketProductItem> products)
    {
        var defaults = products.Where(p => p.IsDefault).ToList();
        if (defaults.Count <= 1) return;
        foreach (var extra in defaults.Skip(1))
        {
            extra.IsDefault = false;
        }
    }
}
