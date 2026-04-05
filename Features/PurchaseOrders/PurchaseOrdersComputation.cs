using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MyBackend.Application.Services;

namespace MyBackend.Features.PurchaseOrders;

internal sealed record PurchaseOrderRow(
    string Company,
    string Id,
    string Number,
    string ShipDate,
    string BillNumber,
    string Name,
    string Description,
    string StatusName,
    decimal TotalAmount,
    IReadOnlyList<PurchaseOrderLineRow> DetailItems);

internal sealed record PurchaseOrderLineRow(
    string Name,
    string ItemCode,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal Discount,
    decimal TotalPrice);

internal static class PurchaseOrdersComputation
{
    public const int MaxDetailsPerCompany = 1000;
    public const int MaxDetailParallelism = 6;

    public static async Task<IReadOnlyList<PurchaseOrderRow>> ComputeForCompany(
        string companyKey,
        IAccurateService service,
        CancellationToken cancellationToken)
    {
        var listRaw = await service.GetPurchaseInvoiceListRaw(companyKey);
        var ids = ParseIdsFromList(listRaw, MaxDetailsPerCompany);
        if (ids.Count == 0) return Array.Empty<PurchaseOrderRow>();

        var rows = new ConcurrentBag<PurchaseOrderRow>();

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDetailParallelism,
                CancellationToken = cancellationToken,
            },
            async (id, token) =>
            {
                string detailRaw;
                try
                {
                    detailRaw = await service.GetPurchaseInvoiceDetailRaw(id, companyKey);
                }
                catch
                {
                    return;
                }

                if (!TryParsePurchaseInvoiceDetail(detailRaw, out var detail))
                    return;

                rows.Add(new PurchaseOrderRow(
                    companyKey,
                    detail.Id,
                    detail.Number,
                    detail.ShipDate,
                    detail.BillNumber,
                    detail.Name,
                    detail.Description,
                    detail.StatusName,
                    detail.TotalAmount,
                    detail.DetailItems));
            });

        return rows
            .OrderByDescending(r => TryParseAccurateDate(r.ShipDate, out var d) ? d : DateOnly.MinValue)
            .ThenByDescending(r => r.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParseIdsFromList(string listRaw, int maxCount)
    {
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(listRaw);
            if (!doc.RootElement.TryGetProperty("d", out var d))
                return result;

            if (d.ValueKind == JsonValueKind.Array)
            {
                CollectIdsFromArray(d, result, maxCount);
                return result;
            }

            if (d.ValueKind == JsonValueKind.Object)
            {
                foreach (var propName in new[] { "rows", "data", "result", "items" })
                {
                    if (!d.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array)
                        continue;
                    CollectIdsFromArray(arr, result, maxCount);
                    if (result.Count > 0)
                        return result;
                }
            }
        }
        catch
        {
            // ignore parse errors
        }

        return result;
    }

    private static void CollectIdsFromArray(JsonElement array, List<string> result, int maxCount)
    {
        foreach (var el in array.EnumerateArray())
        {
            if (result.Count >= maxCount) break;
            if (!el.TryGetProperty("id", out var idEl)) continue;
            var id = idEl.ValueKind switch
            {
                JsonValueKind.Number => idEl.GetRawText(),
                JsonValueKind.String => idEl.GetString(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(id)) result.Add(id);
        }
    }

    private sealed class PurchaseInvoiceDetailDto
    {
        public string Id { get; init; } = "";
        public string Number { get; init; } = "-";
        public string ShipDate { get; init; } = "";
        public string BillNumber { get; init; } = "-";
        public string Name { get; init; } = "-";
        public string Description { get; init; } = "-";
        public string StatusName { get; init; } = "-";
        public decimal TotalAmount { get; init; }
        public IReadOnlyList<PurchaseOrderLineRow> DetailItems { get; init; } = Array.Empty<PurchaseOrderLineRow>();
    }

    private static bool TryParsePurchaseInvoiceDetail(string raw, out PurchaseInvoiceDetailDto detail)
    {
        detail = null!;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object)
                return false;

            var id = GetStringOrNumber(d, "id") ?? "";
            var number = GetStringOrNumber(d, "number") ?? "-";
            var shipDate = GetStringOrNumber(d, "shipDate") ?? GetStringOrNumber(d, "transDate") ?? "";
            var billNumber = GetStringOrNumber(d, "billNumber") ?? "-";
            var description = GetStringOrNumber(d, "description") ?? "-";
            var statusName =
                GetStringOrNumber(d, "statusName")
                ?? GetNestedStringOrNumber(d, "status", "name")
                ?? GetStringOrNumber(d, "status")
                ?? "-";

            var poNumber = GetNestedStringOrNumber(d, "purchaseOrder", "number");
            if (!string.IsNullOrWhiteSpace(poNumber))
                number = poNumber!;

            var name =
                GetStringOrNumber(d, "name")
                ?? GetNestedStringOrNumber(d, "vendor", "name")
                ?? GetNestedStringOrNumber(d, "supplier", "name")
                ?? "-";

            decimal totalAmount =
                GetDecimal(d, "totalAmount")
                ?? GetDecimal(d, "purchaseAmount")
                ?? 0m;

            var detailItems = ParseDetailItems(d);

            detail = new PurchaseInvoiceDetailDto
            {
                Id = id,
                Number = number,
                ShipDate = shipDate,
                BillNumber = billNumber,
                Name = name,
                Description = description,
                StatusName = statusName,
                TotalAmount = totalAmount,
                DetailItems = detailItems,
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<PurchaseOrderLineRow> ParseDetailItems(JsonElement detail)
    {
        foreach (var propName in new[] { "detailItem", "items", "details", "detailItems" })
        {
            if (!detail.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;

            var lines = new List<PurchaseOrderLineRow>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var name =
                    GetNestedStringOrNumber(item, "item", "name")
                    ?? GetStringOrNumber(item, "detailName")
                    ?? GetStringOrNumber(item, "name")
                    ?? "-";

                var itemCode =
                    GetNestedStringOrNumber(item, "item", "no")
                    ?? GetNestedStringOrNumber(item, "item", "code")
                    ?? GetStringOrNumber(item, "itemNo")
                    ?? GetStringOrNumber(item, "itemCode")
                    ?? "-";

                var quantity =
                    GetDecimal(item, "quantity")
                    ?? GetDecimal(item, "qty")
                    ?? GetDecimal(item, "unitQuantity")
                    ?? 0m;

                var unit =
                    GetNestedStringOrNumber(item, "itemUnit", "name")
                    ?? GetNestedStringOrNumber(item, "unit", "name")
                    ?? GetStringOrNumber(item, "unit")
                    ?? "-";

                var unitPrice =
                    GetDecimal(item, "unitPrice")
                    ?? GetDecimal(item, "price")
                    ?? GetDecimal(item, "itemUnitPrice")
                    ?? GetDecimal(item, "pricePerUnit")
                    ?? 0m;

                var discount =
                    GetDecimal(item, "discount")
                    ?? GetDecimal(item, "discountAmount")
                    ?? 0m;

                var totalPrice =
                    GetDecimal(item, "totalUnitPrice")
                    ?? GetDecimal(item, "totalAmount")
                    ?? GetDecimal(item, "amount")
                    ?? GetDecimal(item, "lineTotal")
                    ?? Math.Max((quantity * unitPrice) - discount, 0m);

                lines.Add(new PurchaseOrderLineRow(
                    name,
                    itemCode,
                    quantity,
                    unit,
                    unitPrice,
                    discount,
                    totalPrice));
            }

            return lines;
        }

        return Array.Empty<PurchaseOrderLineRow>();
    }

    private static string? GetStringOrNumber(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static string? GetNestedStringOrNumber(JsonElement obj, string parent, string child)
    {
        if (!obj.TryGetProperty(parent, out var p) || p.ValueKind != JsonValueKind.Object)
            return null;
        return GetStringOrNumber(p, child);
    }

    private static decimal? GetDecimal(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static bool TryParseAccurateDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        foreach (var fmt in new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd" })
        {
            if (DateOnly.TryParseExact(trimmed, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }

        return DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
