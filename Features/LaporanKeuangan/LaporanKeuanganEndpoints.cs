using System.Security.Claims;
using System.Text.Json;
using MyBackend.Application.Services;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MyBackend.Features.LaporanKeuangan;

public static class LaporanKeuanganEndpoints
{
    private static readonly string[] DashboardOverviewCoaNos = ["4101", "1103", "2101", "2102", "2103", "5100", "6100", "6200", "6300"];

    private sealed record LabaRugiPdfRow(
        string AccountNo,
        string AccountName,
        bool IsParent,
        string ParentNo,
        decimal Amount,
        int Lvl
    );

    private static readonly string[] LabaRugiParentOrder =
    [
        "4101", "5100", "6100", "6200", "6300", "7100", "8100", "8200", "8300"
    ];

    private static List<LabaRugiPdfRow> ParseLabaRugiRows(JsonElement dataElement)
    {
        var rows = new List<LabaRugiPdfRow>();
        if (dataElement.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var item in dataElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            static string Str(JsonElement e, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (e.TryGetProperty(k, out var p))
                    {
                        if (p.ValueKind == JsonValueKind.String) return p.GetString() ?? "";
                        if (p.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) return p.ToString();
                    }
                }
                return "";
            }

            static decimal Dec(JsonElement e, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (e.TryGetProperty(k, out var p))
                    {
                        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
                        if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var ds)) return ds;
                    }
                }
                return 0m;
            }

            static int Int(JsonElement e, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (e.TryGetProperty(k, out var p))
                    {
                        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i;
                        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var si)) return si;
                    }
                }
                return 0;
            }

            static bool Bool(JsonElement e, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (e.TryGetProperty(k, out var p))
                    {
                        if (p.ValueKind == JsonValueKind.True) return true;
                        if (p.ValueKind == JsonValueKind.False) return false;
                        if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var bs)) return bs;
                    }
                }
                return false;
            }

            var accountNo = Str(item, "accountNo", "no");
            var accountName = Str(item, "accountName", "name");
            var parentNo = Str(item, "parentNo", "parent");
            var amount = Dec(item, "amount");
            var lvl = Int(item, "lvl");
            var isParent = Bool(item, "isParent");

            rows.Add(new LabaRugiPdfRow(accountNo, accountName, isParent, parentNo, amount, lvl));
        }

        return rows;
    }

    private static decimal GetParentAmount(List<LabaRugiPdfRow> rows, string parentNo)
        => rows.FirstOrDefault(r => r.IsParent && r.AccountNo == parentNo)?.Amount ?? 0m;

    private static byte[] BuildLabaRugiPdf(
        string periodLabel,
        List<(string CompanyName, List<LabaRugiPdfRow> Rows)> blocks)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(header =>
                {
                    header.Item().Text("Laporan Laba Rugi")
                        .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                    header.Item().Text($"Periode: {periodLabel}")
                        .FontSize(10).FontColor(Colors.Grey.Darken1);
                    header.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Blue.Medium);
                });

                page.Content().Column(content =>
                {
                    foreach (var block in blocks)
                    {
                        var rows = block.Rows;
                        var totalPendapatan = GetParentAmount(rows, "4101");
                        var safePendapatan = totalPendapatan == 0 ? 1 : totalPendapatan;
                        var hpp = GetParentAmount(rows, "5100");
                        var labaKotor = totalPendapatan - hpp;
                        var bebanOp = GetParentAmount(rows, "6100") + GetParentAmount(rows, "6200") + GetParentAmount(rows, "6300");
                        var labaUsaha = labaKotor - bebanOp;
                        var pendLain = GetParentAmount(rows, "7100");
                        var bebanLain = GetParentAmount(rows, "8100") + GetParentAmount(rows, "8200") + GetParentAmount(rows, "8300");
                        var labaBersih = labaUsaha + pendLain - bebanLain;

                        content.Item().PaddingTop(10).Text(block.CompanyName)
                            .Bold().FontSize(13).FontColor(Colors.Grey.Darken3);

                        foreach (var parentNo in LabaRugiParentOrder)
                        {
                            var parent = rows.FirstOrDefault(r => r.IsParent && r.AccountNo == parentNo);
                            if (parent is null) continue;
                            var children = rows.Where(r => !r.IsParent && r.ParentNo == parentNo).ToList();
                            var parentAmount = parent.Amount;

                            content.Item().PaddingTop(6).Text(parent.AccountName.ToUpperInvariant())
                                .Bold().FontSize(11);

                            content.Item().Table(table =>
                            {
                                table.ColumnsDefinition(col =>
                                {
                                    col.RelativeColumn(3);
                                    col.RelativeColumn(2);
                                    col.RelativeColumn(1);
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Background(Colors.Blue.Lighten5).Padding(4).Text("Akun").Bold();
                                    h.Cell().Background(Colors.Blue.Lighten5).Padding(4).AlignRight().Text("Jumlah").Bold();
                                    h.Cell().Background(Colors.Blue.Lighten5).Padding(4).AlignRight().Text("% Pendapatan").Bold();
                                });

                                foreach (var c in children)
                                {
                                    var pct = c.Amount == 0 ? 0 : (c.Amount / safePendapatan) * 100m;
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(c.AccountName);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight()
                                        .Text($"{c.Amount:N0}");
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight()
                                        .Text(c.Amount == 0 ? "-" : $"{pct:N2}%");
                                }

                                var pPct = parentAmount == 0 ? 0 : (parentAmount / safePendapatan) * 100m;
                                table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text($"TOTAL {parent.AccountName}").Bold();
                                table.Cell().Background(Colors.Grey.Lighten4).Padding(4).AlignRight().Text($"{parentAmount:N0}").Bold();
                                table.Cell().Background(Colors.Grey.Lighten4).Padding(4).AlignRight()
                                    .Text(parentAmount == 0 ? "-" : $"{pPct:N2}%").Bold();
                            });

                            if (parentNo == "5100")
                                content.Item().PaddingTop(4).Text($"Laba Kotor: {labaKotor:N0}").Bold();
                            if (parentNo == "6300")
                                content.Item().PaddingTop(4).Text($"Laba Usaha: {labaUsaha:N0}").Bold();
                        }

                        content.Item().PaddingVertical(8).Background(Colors.Blue.Darken2).Padding(6)
                            .Text($"LABA BERSIH: {labaBersih:N0}")
                            .Bold().FontColor(Colors.White);
                    }
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// Registers Laporan Keuangan API routes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GET <c>/api/laporan-keuangan/laba-rugi</c>: when two or more companies are authorized,
    /// the response is <c>{ s: true, companies: [ { companyName, data }, ... ] }</c>.
    /// Each <c>data</c> is an independent Accurate P&amp;L account tree; clients must not merge
    /// detail rows across entities by account number alone.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapLaporanKeuanganEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/laporan-keuangan/laba-rugi", async (
            ClaimsPrincipal user,
            string fromDate,
            string toDate,
            string[]? company,
            IAccurateService service,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            var companyValues = company ?? Array.Empty<string>();
            var accessResult = await access.NormalizeAndAuthorizeAsync(
                user,
                companyValues,
                cancellationToken);
            if (!accessResult.Success)
                return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

            var keys = accessResult.AccurateCompanyKeys;
            try
            {
                if (keys.Count >= 2)
                {
                    var companiesList = new List<object>();
                    foreach (var companyName in keys)
                    {
                        var rawJson = await service.GetPlAccountAmountRaw(fromDate, toDate, companyName);
                        using var doc = JsonDocument.Parse(rawJson);
                        var d = doc.RootElement.TryGetProperty("d", out var prop)
                            ? prop.Clone()
                            : JsonDocument.Parse("null").RootElement.Clone();
                        companiesList.Add(new { companyName, data = d });
                    }

                    return Results.Json(new { s = true, companies = companiesList });
                }

                var singleCompany = keys.Count == 1 ? keys[0] : null;
                var json = await service.GetPlAccountAmountRaw(fromDate, toDate, singleCompany);
                return Results.Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        })
            .RequireAuthorization()
            .WithTags("Laporan Keuangan");

        app.MapGet("/api/laporan-keuangan/laba-rugi/pdf", async (
            ClaimsPrincipal user,
            string fromDate,
            string toDate,
            string[]? company,
            IAccurateService service,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            var companyValues = company ?? Array.Empty<string>();
            var accessResult = await access.NormalizeAndAuthorizeAsync(
                user,
                companyValues,
                cancellationToken);
            if (!accessResult.Success)
                return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

            var keys = accessResult.AccurateCompanyKeys;
            try
            {
                var blocks = new List<(string CompanyName, List<LabaRugiPdfRow> Rows)>();
                foreach (var companyName in keys)
                {
                    var rawJson = await service.GetPlAccountAmountRaw(fromDate, toDate, companyName);
                    using var doc = JsonDocument.Parse(rawJson);
                    if (!doc.RootElement.TryGetProperty("d", out var d))
                        continue;

                    blocks.Add((companyName, ParseLabaRugiRows(d)));
                }

                if (blocks.Count == 0)
                    return Results.BadRequest(new { s = false, d = "Tidak ada data untuk diexport ke PDF." });

                var periodLabel = $"{fromDate} - {toDate}";
                var pdfBytes = BuildLabaRugiPdf(periodLabel, blocks);
                var fileName = $"Laporan_Laba_Rugi_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
                return Results.File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        })
            .RequireAuthorization()
            .WithTags("Laporan Keuangan");

        app.MapGet("/api/laporan-keuangan/neraca", async (
            ClaimsPrincipal user,
            string asOfDate,
            HttpRequest request,
            IAccurateService service,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            var companyValues = request.Query["company"].ToArray();
            var accessResult = await access.NormalizeAndAuthorizeAsync(
                user,
                companyValues,
                cancellationToken);
            if (!accessResult.Success)
                return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

            var keys = accessResult.AccurateCompanyKeys;
            try
            {
                if (keys.Count >= 2)
                {
                    var companiesList = new List<object>();
                    foreach (var companyName in keys)
                    {
                        var rawJson = await service.GetBsAccountAmountRaw(asOfDate, companyName);
                        using var doc = JsonDocument.Parse(rawJson);
                        var d = doc.RootElement.TryGetProperty("d", out var prop)
                            ? prop.Clone()
                            : JsonDocument.Parse("null").RootElement.Clone();
                        companiesList.Add(new { companyName, data = d });
                    }

                    return Results.Json(new { s = true, companies = companiesList });
                }

                var singleCompany = keys.Count == 1 ? keys[0] : null;
                var json = await service.GetBsAccountAmountRaw(asOfDate, singleCompany);
                return Results.Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        })
            .RequireAuthorization()
            .WithTags("Laporan Keuangan");

        app.MapGet("/api/laporan-keuangan/arus-kas", async (
            ClaimsPrincipal user,
            string fromDate,
            string toDate,
            string[]? company,
            int? maxIds,
            bool? debug,
            IAccurateService service,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParseExact(fromDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
                return Results.Json(new { s = false, d = "Invalid fromDate; use dd/MM/yyyy" }, statusCode: 400);
            if (!DateOnly.TryParseExact(toDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
                return Results.Json(new { s = false, d = "Invalid toDate; use dd/MM/yyyy" }, statusCode: 400);
            if (from > to)
                return Results.Json(new { s = false, d = "fromDate must be <= toDate" }, statusCode: 400);

            var companyValues = company ?? Array.Empty<string>();
            var accessResult = await access.NormalizeAndAuthorizeAsync(
                user,
                companyValues,
                cancellationToken);
            if (!accessResult.Success)
                return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

            var keys = accessResult.AccurateCompanyKeys;
            try
            {
                var result = await CashFlowComputation.ComputeAsync(
                    keys,
                    from,
                    to,
                    service,
                    cancellationToken,
                    maxIds);

                return Results.Json(new
                {
                    s = true,
                    fromDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    toDate = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    companyScope = keys,
                    totalCashIn = result.TotalCashIn,
                    totalCashOut = result.TotalCashOut,
                    netCashFlow = result.NetCashFlow,
                    months = result.Rows.Select(r => new
                    {
                        month = r.Month,
                        cashIn = r.CashIn,
                        cashOut = r.CashOut,
                        net = r.Net,
                        cumulative = r.Cumulative,
                    }),
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        })
            .RequireAuthorization()
            .WithTags("Laporan Keuangan");

        app.MapGet("/api/dashboard/arus-kas-proyeksi", async (
            ClaimsPrincipal user,
            string asOfDate,
            string[]? company,
            IAccurateService service,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParseExact(asOfDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
                return Results.Json(new { s = false, d = "Invalid asOfDate; use dd/MM/yyyy" }, statusCode: 400);

            var companyValues = company ?? Array.Empty<string>();
            var accessResult = await access.NormalizeAndAuthorizeAsync(
                user,
                companyValues,
                cancellationToken);
            if (!accessResult.Success)
                return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

            var keys = accessResult.AccurateCompanyKeys;
            try
            {
                var from = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-5);
                var result = await CashFlowComputation.ComputeAsync(
                    keys,
                    from,
                    asOf,
                    service,
                    cancellationToken,
                    maxIdsPerCompany: 1200);

                var actualRows = result.Rows
                    .OrderBy(r => r.Month, StringComparer.Ordinal)
                    .ToList();

                if (actualRows.Count == 0)
                {
                    return Results.Json(new
                    {
                        s = true,
                        asOfDate = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        companyScope = keys,
                        points = Array.Empty<object>(),
                        projectedBalance = 0m,
                    });
                }

                var baseRows = actualRows.Skip(Math.Max(0, actualRows.Count - 3)).ToList();
                decimal avgInDelta = 0m;
                decimal avgOutDelta = 0m;
                if (baseRows.Count >= 2)
                {
                    decimal sumInDelta = 0m;
                    decimal sumOutDelta = 0m;
                    for (var i = 1; i < baseRows.Count; i++)
                    {
                        sumInDelta += baseRows[i].CashIn - baseRows[i - 1].CashIn;
                        sumOutDelta += baseRows[i].CashOut - baseRows[i - 1].CashOut;
                    }
                    avgInDelta = sumInDelta / (baseRows.Count - 1);
                    avgOutDelta = sumOutDelta / (baseRows.Count - 1);
                }

                var last = actualRows[^1];
                var prevIn = last.CashIn;
                var prevOut = last.CashOut;
                var prevCumulative = last.Cumulative;
                var projectedRows = new List<CashFlowMonthlyRow>(capacity: 3);

                for (var step = 1; step <= 3; step++)
                {
                    var dt = DateOnly.ParseExact(last.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(step);
                    var month = dt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    var cashIn = Math.Max(0m, Math.Round(prevIn + avgInDelta, 0));
                    var cashOut = Math.Max(0m, Math.Round(prevOut + avgOutDelta, 0));
                    var net = cashIn - cashOut;
                    var cumulative = prevCumulative + net;
                    projectedRows.Add(new CashFlowMonthlyRow(month, cashIn, cashOut, net, cumulative));
                    prevIn = cashIn;
                    prevOut = cashOut;
                    prevCumulative = cumulative;
                }

                var points = actualRows
                    .Select(r => new
                    {
                        month = r.Month,
                        cashIn = r.CashIn,
                        cashOut = r.CashOut,
                        net = r.Net,
                        cumulative = r.Cumulative,
                        isProjected = false,
                    })
                    .Concat(projectedRows.Select(r => new
                    {
                        month = r.Month,
                        cashIn = r.CashIn,
                        cashOut = r.CashOut,
                        net = r.Net,
                        cumulative = r.Cumulative,
                        isProjected = true,
                    }))
                    .ToList();

                return Results.Json(new
                {
                    s = true,
                    asOfDate = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    companyScope = keys,
                    points,
                    projectedBalance = points.Count > 0 ? points[^1].cumulative : 0m,
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        })
            .RequireAuthorization()
            .WithTags("Dashboard");

        app.MapGet("/api/dashboard/overview-cards", async (
            ClaimsPrincipal user,
            string[]? company,
            IAccurateService service,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            var companyValues = company ?? Array.Empty<string>();
            var accessResult = await access.NormalizeAndAuthorizeAsync(
                user,
                companyValues,
                cancellationToken);
            if (!accessResult.Success)
                return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

            var keys = accessResult.AccurateCompanyKeys;
            decimal totalPendapatan = 0m;
            decimal totalPiutang = 0m;
            decimal totalHutang = 0m;
            decimal totalBeban = 0m;
            var gate = new object();

            try
            {
                await Parallel.ForEachAsync(
                    keys,
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
                    async (companyName, token) =>
                    {
                        decimal pendapatan = 0m;
                        decimal piutang = 0m;
                        decimal hutang = 0m;
                        decimal beban = 0m;

                        foreach (var coaNo in DashboardOverviewCoaNos)
                        {
                            token.ThrowIfCancellationRequested();
                            string raw;
                            try
                            {
                                raw = await service.GetCoaDetailRaw(coaNo, companyName);
                            }
                            catch
                            {
                                continue;
                            }

                            var balance = TryParseCoaBalance(raw);
                            switch (coaNo)
                            {
                                case "4101":
                                    pendapatan += balance;
                                    break;
                                case "1103":
                                    piutang += balance;
                                    break;
                                case "2101":
                                case "2102":
                                case "2103":
                                    hutang += balance;
                                    break;
                                case "5100":
                                case "6100":
                                case "6200":
                                case "6300":
                                    beban += balance;
                                    break;
                            }
                        }

                        lock (gate)
                        {
                            totalPendapatan += pendapatan;
                            totalPiutang += piutang;
                            totalHutang += hutang;
                            totalBeban += beban;
                        }
                    });

                return Results.Json(new
                {
                    s = true,
                    companyScope = keys,
                    totalPendapatan,
                    totalPiutang,
                    totalHutang,
                    labaBersih = totalPendapatan - totalBeban,
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        })
            .RequireAuthorization()
            .WithTags("Dashboard");

        app.MapGet("/api/reports/summary", async (
            ClaimsPrincipal user,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            var allowed = await access.GetAllowedAccurateCompanyKeysAsync(user, cancellationToken);
            return Results.Json(new
            {
                message = "Contoh endpoint laporan berbasis hak akses perusahaan.",
                allowedAccurateCompanyKeys = allowed,
            });
        })
            .RequireAuthorization()
            .WithTags("Reports");

        return app;
    }

    private static decimal TryParseCoaBalance(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return 0;

            if (!doc.RootElement.TryGetProperty("d", out var d))
                return 0;
            if (d.ValueKind != JsonValueKind.Object)
                return 0;
            if (!d.TryGetProperty("balance", out var b))
                return 0;

            return b.ValueKind switch
            {
                JsonValueKind.Number => b.GetDecimal(),
                JsonValueKind.String when decimal.TryParse(b.GetString(), out var p) => p,
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }
}
