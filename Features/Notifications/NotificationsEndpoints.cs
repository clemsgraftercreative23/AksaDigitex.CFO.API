using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MyBackend.Application.Options;
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Features.Notifications;

public static class NotificationsEndpoints
{
    /// <summary>Cache TTL for overdue notifications (per-user).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public sealed class SendTestEmailRequest
    {
        public string ToEmail { get; set; } = string.Empty;
        public string? ToName { get; set; }
                public string Subject { get; set; } = "PERINGATAN JATUH TEMPO: H-{H_MINUS}!";
                public bool UseTemplate { get; set; } = true;
                public string? HtmlBody { get; set; }
                public string? PlainTextBody { get; set; }
                public string JenisTagihan { get; set; } = "{jenis_tagihan}";
                public string BankName { get; set; } = "{bank_name}";
                public string TanggalJatuhTempo { get; set; } = "{tanggal_jatuh_tempo}";
                public string Nominal { get; set; } = "{nominal}";
                public string DocumentFileName { get; set; } = "Dokumen_Tagihan_{bank_name}.pdf";
                public string FileSize { get; set; } = "1.4 MB";
                public string ReminderTime { get; set; } = "10:00 WIB";
                public string DownloadUrl { get; set; } = "#";
    }

    public sealed class SendPreDueEmailRequest
    {
        public List<string>? Recipients { get; set; }
        public List<int>? ReminderDaysBeforeDue { get; set; }
        public bool TemplateOnly { get; set; }
    }

    public sealed class SendOverdueEmailRequest
    {
        public List<string>? Recipients { get; set; }
        public int MinimumDaysPastDue { get; set; } = 30;
        public bool TemplateOnly { get; set; }
    }

    public sealed class ProcurementItemDto
    {
        public string No { get; set; } = "1";
        public string Deskripsi { get; set; } = "{item_1}";
        public string Qty { get; set; } = "{qty_1}";
        public string Satuan { get; set; } = "{satuan_1}";
        public string HargaSatuan { get; set; } = "{harga_1}";
        public string Total { get; set; } = "{total_1}";
    }

    public sealed class SendProcurementEmailRequest
    {
        public List<string>? Recipients { get; set; }
        public string Subject { get; set; } = "PERMINTAAN PERSETUJUAN PENGADAAN";
        public string TanggalPengajuan { get; set; } = "{tanggal_pengajuan}";
        public string NoPurchaseRequest { get; set; } = "{no_purchase_request}";
        public string NamaEntitas { get; set; } = "{nama_entitas}";
        public string NamaDepartemen { get; set; } = "{nama_departemen}";
        public string NamaPic { get; set; } = "{nama_pic}";
        public string KategoriPengadaan { get; set; } = "{kategori_pengadaan}";
        public string NamaVendor { get; set; } = "{nama_vendor}";
        public string TanggalKebutuhan { get; set; } = "{tanggal_kebutuhan}";
        public string AnggaranTersedia { get; set; } = "{anggaran_tersedia}";
        public string GrandTotal { get; set; } = "{grand_total}";
        public string JustifikasiPengadaan { get; set; } = "{alasan_pengadaan}";
        public string MetodePembayaran { get; set; } = "{metode_pembayaran}";
        public string TerminPembayaran { get; set; } = "{termin_pembayaran}";
        public string BudgetCode { get; set; } = "{budget_code}";
        public string StatusApproval { get; set; } = "Menunggu Persetujuan CFO";
        public string DokumenFileName { get; set; } = "Quotation_{nama_vendor}_{no_purchase_request}.pdf";
        public string DokumenCaption { get; set; } = "Lampiran: Penawaran Vendor, Spesifikasi Teknis";
        public string DownloadUrl { get; set; } = "#";
        public List<ProcurementItemDto>? Items { get; set; }
    }

        private static string BuildDefaultEmailHtml(SendTestEmailRequest request)
        {
                return $$"""
<!doctype html>
<html lang="id">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{request.Subject}}</title>
</head>
<body style="margin:0;padding:0;background:#f3f4f6;font-family:Arial,Helvetica,sans-serif;color:#374151;">
    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="padding:18px 0;background:#f3f4f6;">
        <tr>
            <td align="center">
                <table role="presentation" width="760" cellpadding="0" cellspacing="0" style="width:760px;max-width:760px;background:#ffffff;border:1px solid #e5e7eb;">
                    <tr>
                        <td style="background:#d9534f;color:#ffffff;padding:14px 22px;">
                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                                <tr>
                                    <td style="font-size:16px;font-weight:700;">PERINGATAN JATUH TEMPO: H-{H_MINUS}</td>
                                    <td align="right" style="font-size:15px;font-weight:700;">{{request.ReminderTime}}</td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:30px 38px 24px 38px;">
                            <h1 style="margin:0 0 6px 0;text-align:center;font-size:52px;line-height:1.1;color:#1f2937;">Dashboard CFO</h1>
                            <p style="margin:0 0 28px 0;text-align:center;font-size:16px;color:#9ca3af;">Sistem Otomatisasi Pengingat Pembayaran (Bunga/Prioritas)</p>

                            <p style="margin:0 0 14px 0;font-size:18px;line-height:1.6;">Yth. Tim Finance &amp; Direksi,</p>
                            <p style="margin:0 0 24px 0;font-size:18px;line-height:1.7;">Kami menginformasikan bahwa terdapat dokumen tagihan masuk dari perbankan yang akan segera jatuh tempo (H-{H_MINUS}). Mohon periksa lampiran PDF di bawah ini untuk rincian lebih lanjut.</p>

                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #e5e7eb;border-radius:8px;overflow:hidden;">
                                <tr>
                                    <td style="padding:14px 18px;font-size:16px;color:#6b7280;width:40%;border-bottom:1px solid #eef2f7;">Entitas:</td>
                                    <td style="padding:14px 18px;font-size:16px;font-weight:700;color:#1f2937;border-bottom:1px solid #eef2f7;">{nama_entitas}</td>
                                </tr>
                                <tr>
                                    <td style="padding:14px 18px;font-size:16px;color:#6b7280;width:40%;border-bottom:1px solid #eef2f7;">Jenis Tagihan:</td>
                                    <td style="padding:14px 18px;font-size:16px;font-weight:700;color:#1f2937;border-bottom:1px solid #eef2f7;">{{request.JenisTagihan}}</td>
                                </tr>
                                <tr>
                                    <td style="padding:14px 18px;font-size:16px;color:#6b7280;width:40%;border-bottom:1px solid #eef2f7;">Bank Terkait:</td>
                                    <td style="padding:14px 18px;font-size:16px;font-weight:700;color:#1f2937;border-bottom:1px solid #eef2f7;">{{request.BankName}}</td>
                                </tr>
                                <tr>
                                    <td style="padding:14px 18px;font-size:16px;color:#6b7280;width:40%;border-bottom:1px solid #eef2f7;">Jatuh Tempo:</td>
                                    <td style="padding:14px 18px;font-size:16px;font-weight:700;color:#d9534f;border-bottom:1px solid #eef2f7;">{{request.TanggalJatuhTempo}}</td>
                                </tr>
                                <tr>
                                    <td style="padding:14px 18px;font-size:16px;color:#6b7280;width:40%;">Total Tagihan:</td>
                                    <td style="padding:14px 18px;font-size:18px;font-weight:800;color:#1f2937;">Rp {{request.Nominal}}</td>
                                </tr>
                            </table>

                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:26px;border:2px dashed #d1d5db;border-radius:10px;background:#fafafa;">
                                <tr>
                                    <td align="center" style="padding:28px 20px;">
                                        <div style="font-size:44px;line-height:1;margin-bottom:10px;">📄</div>
                                        <div style="font-size:18px;font-weight:800;color:#1f2937;">{{request.DocumentFileName}}</div>
                                        <div style="font-size:14px;color:#9ca3af;margin-top:6px;">Ukuran file: {{request.FileSize}}</div>
                                        <a href="{{request.DownloadUrl}}" style="display:inline-block;margin-top:16px;background:#e74c3c;color:#ffffff;text-decoration:none;padding:10px 24px;border-radius:6px;font-size:16px;font-weight:800;">DOWNLOAD PDF</a>
                                    </td>
                                </tr>
                            </table>

                            <p style="margin:22px 0 0 0;text-align:center;font-size:14px;color:#9ca3af;font-style:italic;">*Harap lakukan pengecekan saldo pada Cash of Account sebelum melakukan eksekusi pembayaran.</p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";
        }

        private static string BuildDefaultPlainText(SendTestEmailRequest request)
        {
                return string.Join(Environment.NewLine,
                        "Dashboard CFO - Peringatan Jatuh Tempo",
                        "",
                        $"Jenis Tagihan: {request.JenisTagihan}",
                        $"Bank Terkait: {request.BankName}",
                        $"Jatuh Tempo: {request.TanggalJatuhTempo}",
                        $"Total Tagihan: Rp {request.Nominal}",
                        $"Dokumen: {request.DocumentFileName} ({request.FileSize})",
                        $"Download: {request.DownloadUrl}");
        }

        private static string BuildProcurementEmailHtml(SendProcurementEmailRequest request)
        {
                var rows = (request.Items is { Count: > 0 }
                        ? request.Items
                        :
                        [
                                new ProcurementItemDto
                                {
                                        No = "1",
                                        Deskripsi = "{item_1}",
                                        Qty = "{qty_1}",
                                        Satuan = "{satuan_1}",
                                        HargaSatuan = "{harga_1}",
                                        Total = "{total_1}",
                                },
                                new ProcurementItemDto
                                {
                                        No = "2",
                                        Deskripsi = "{item_2}",
                                        Qty = "{qty_2}",
                                        Satuan = "{satuan_2}",
                                        HargaSatuan = "{harga_2}",
                                        Total = "{total_2}",
                                },
                                new ProcurementItemDto
                                {
                                        No = "3",
                                        Deskripsi = "{item_3}",
                                        Qty = "{qty_3}",
                                        Satuan = "{satuan_3}",
                                        HargaSatuan = "{harga_3}",
                                        Total = "{total_3}",
                                },
                        ]);

                var itemRows = string.Join(string.Empty, rows.Select(x => $$"""
                                <tr>
                                    <td style="padding:7px 8px;border-top:1px solid #dfe7df;color:#334155;">{{Escape(x.No)}}</td>
                                    <td style="padding:7px 8px;border-top:1px solid #dfe7df;color:#0f172a;font-weight:700;">{{Escape(x.Deskripsi)}}</td>
                                    <td style="padding:7px 8px;border-top:1px solid #dfe7df;color:#64748b;">{{Escape(x.Qty)}}</td>
                                    <td style="padding:7px 8px;border-top:1px solid #dfe7df;color:#64748b;">{{Escape(x.Satuan)}}</td>
                                    <td style="padding:7px 8px;border-top:1px solid #dfe7df;color:#334155;">Rp {{Escape(x.HargaSatuan)}}</td>
                                    <td style="padding:7px 8px;border-top:1px solid #dfe7df;color:#0f172a;font-weight:700;">Rp {{Escape(x.Total)}}</td>
                                </tr>
"""));

                return $$"""
<!doctype html>
<html lang="id">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{Escape(request.Subject)}}</title>
</head>
<body style="margin:0;padding:0;background:#eef2f7;font-family:Arial,Helvetica,sans-serif;color:#334155;">
    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="padding:12px 0;background:#eef2f7;">
        <tr>
            <td align="center">
                <table role="presentation" width="380" cellpadding="0" cellspacing="0" style="width:380px;max-width:380px;background:#ffffff;border:1px solid #d9e5d9;border-radius:8px;overflow:hidden;box-shadow:0 4px 18px rgba(15,23,42,0.08);">
                    <tr>
                        <td style="background:#166534;color:#ffffff;padding:10px 14px;">
                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                                <tr>
                                    <td style="font-size:11px;font-weight:700;letter-spacing:0.4px;">&#9632; {{Escape(request.Subject)}}</td>
                                    <td align="right" style="font-size:10px;font-weight:700;">{{Escape(request.TanggalPengajuan)}}</td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:18px 14px 12px 14px;">
                            <div style="text-align:center;margin-bottom:16px;">
                                <div style="font-size:9px;letter-spacing:1.7px;color:#14532d;font-weight:700;">SMART GARMENT HOLDING</div>
                                <div style="font-size:30px;line-height:1;color:#14532d;margin:2px 0 4px 0;">Dashboard CFO</div>
                                <div style="font-size:9px;color:#94a3b8;">Sistem Pengadaan Barang &amp; Jasa</div>
                            </div>

                            <div style="font-size:10px;line-height:1.65;color:#334155;margin-bottom:12px;">
                                Yth. CFO &amp; Tim Finance,<br><br>
                                Terdapat pengajuan pengadaan baru yang memerlukan persetujuan anggaran. Mohon tinjau detail pengadaan berikut dan berikan keputusan sebelum batas waktu yang ditentukan.
                            </div>

                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #dbe7db;border-radius:6px;overflow:hidden;font-size:9px;line-height:1.35;">
                                <tr style="background:#f2f8f2;color:#64748b;font-weight:700;">
                                    <td style="padding:7px 8px;border-bottom:1px solid #dbe7db;">FIELD</td>
                                    <td style="padding:7px 8px;border-bottom:1px solid #dbe7db;">DETAIL</td>
                                </tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">No. Purchase Request</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.NoPurchaseRequest)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">Entitas</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.NamaEntitas)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">Departemen Pemohon</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.NamaDepartemen)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">PIC Pemohon</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.NamaPic)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">Kategori Pengadaan</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.KategoriPengadaan)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">Vendor / Supplier</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.NamaVendor)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">Tanggal Kebutuhan</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;color:#dc2626;">{{Escape(request.TanggalKebutuhan)}}</td></tr>
                                <tr><td style="padding:7px 8px;color:#64748b;">Anggaran Tersedia</td><td style="padding:7px 8px;font-weight:700;">Rp {{Escape(request.AnggaranTersedia)}}</td></tr>
                            </table>

                            <div style="margin-top:14px;font-size:11px;color:#0f172a;font-weight:700;letter-spacing:0.3px;">RINCIAN ITEM PENGADAAN</div>
                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:6px;border:1px solid #dbe7db;border-radius:6px;overflow:hidden;font-size:9px;line-height:1.35;">
                                <tr style="background:#166534;color:#ffffff;font-weight:700;">
                                    <td style="padding:7px 6px;">NO.</td>
                                    <td style="padding:7px 6px;">DESKRIPSI ITEM</td>
                                    <td style="padding:7px 6px;">QTY</td>
                                    <td style="padding:7px 6px;">SATUAN</td>
                                    <td style="padding:7px 6px;">HARGA SATUAN</td>
                                    <td style="padding:7px 6px;">TOTAL</td>
                                </tr>
{{itemRows}}
                                <tr style="background:#ecfdf3;">
                                    <td colspan="5" align="right" style="padding:8px 8px;color:#14532d;font-weight:700;">Grand Total:</td>
                                    <td style="padding:8px 8px;color:#14532d;font-weight:700;">Rp {{Escape(request.GrandTotal)}}</td>
                                </tr>
                            </table>

                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:10px;border:1px solid #dbe7db;border-radius:6px;overflow:hidden;font-size:9px;line-height:1.35;">
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;width:42%;">Justifikasi Pengadaan</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.JustifikasiPengadaan)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">Metode Pembayaran</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.MetodePembayaran)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">Termin Pembayaran</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.TerminPembayaran)}}</td></tr>
                                <tr><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;color:#64748b;">Budget Code</td><td style="padding:7px 8px;border-bottom:1px solid #e8efe8;font-weight:700;">{{Escape(request.BudgetCode)}}</td></tr>
                                <tr><td style="padding:7px 8px;color:#64748b;">Status Approval</td><td style="padding:7px 8px;"><span style="display:inline-block;padding:2px 8px;border-radius:999px;background:#fff7ed;color:#ea580c;font-weight:700;">{{Escape(request.StatusApproval)}}</span></td></tr>
                            </table>

                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:14px;border:1px dashed #86efac;border-radius:8px;background:#f7fee7;">
                                <tr>
                                    <td align="center" style="padding:14px 10px;">
                                        <div style="font-size:22px;line-height:1;margin-bottom:8px;color:#166534;">&#128196;</div>
                                        <div style="font-size:9px;font-weight:700;color:#14532d;">{{Escape(request.DokumenFileName)}}</div>
                                        <div style="font-size:7px;color:#94a3b8;margin-top:2px;">{{Escape(request.DokumenCaption)}}</div>
                                        <a href="{{Escape(request.DownloadUrl)}}" style="display:inline-block;margin-top:8px;background:#166534;color:#ffffff;text-decoration:none;padding:7px 16px;border-radius:5px;font-size:8px;font-weight:700;">&#11015; DOWNLOAD DOKUMEN</a>
                                    </td>
                                </tr>
                            </table>

                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:12px;">
                                <tr>
                                    <td align="left" style="width:50%;padding-right:6px;">
                                        <a href="#" style="display:block;text-align:center;background:#166534;color:#ffffff;text-decoration:none;padding:8px 10px;border-radius:4px;font-size:9px;font-weight:700;">&#10003; APPROVE PENGADAAN</a>
                                    </td>
                                    <td align="right" style="width:50%;padding-left:6px;">
                                        <a href="#" style="display:block;text-align:center;background:#ef4444;color:#ffffff;text-decoration:none;padding:8px 10px;border-radius:4px;font-size:9px;font-weight:700;">&#10007; TOLAK / REVISI</a>
                                    </td>
                                </tr>
                            </table>

                            <div style="margin-top:10px;text-align:center;font-size:8px;line-height:1.4;color:#94a3b8;font-style:italic;">
                                @Persetujuan wajib diberikan paling lambat batas approval untuk memenuhi jadwal pengadaan.
                            </div>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";
        }

        private static string BuildProcurementPlainText(SendProcurementEmailRequest request)
        {
                var rows = request.Items is { Count: > 0 }
                        ? request.Items
                        : [new ProcurementItemDto(), new ProcurementItemDto { No = "2", Deskripsi = "{item_2}" }, new ProcurementItemDto { No = "3", Deskripsi = "{item_3}" }];

                var lines = new List<string>
                {
                        request.Subject,
                        "",
                        $"No. Purchase Request: {request.NoPurchaseRequest}",
                        $"Entitas: {request.NamaEntitas}",
                        $"Departemen Pemohon: {request.NamaDepartemen}",
                        $"PIC Pemohon: {request.NamaPic}",
                        $"Kategori Pengadaan: {request.KategoriPengadaan}",
                        $"Vendor: {request.NamaVendor}",
                        $"Tanggal Kebutuhan: {request.TanggalKebutuhan}",
                        $"Anggaran Tersedia: Rp {request.AnggaranTersedia}",
                        "",
                        "Rincian Item:",
                };

                lines.AddRange(rows.Select(x => $"- {x.No}. {x.Deskripsi} | qty {x.Qty} {x.Satuan} | Rp {x.HargaSatuan} | total Rp {x.Total}"));
                lines.Add($"Grand Total: Rp {request.GrandTotal}");
                lines.Add("");
                lines.Add($"Justifikasi: {request.JustifikasiPengadaan}");
                lines.Add($"Metode Pembayaran: {request.MetodePembayaran}");
                lines.Add($"Termin Pembayaran: {request.TerminPembayaran}");
                lines.Add($"Budget Code: {request.BudgetCode}");
                lines.Add($"Status Approval: {request.StatusApproval}");
                lines.Add($"Dokumen: {request.DokumenFileName}");
                lines.Add($"Download: {request.DownloadUrl}");

                return string.Join(Environment.NewLine, lines);
        }

    private static string Escape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notifications/overdue", async (
                ClaimsPrincipal user,
                AccurateHttpClient accurateClient,
                IAccurateService accurateService,
                ICompanyAccessService access,
                IMemoryCache cache,
                ILogger<OverdueNotificationDto> logger,
                CancellationToken ct) =>
            {
                // Resolve user identity for cache key
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
                var cacheKey = $"overdue-notifs:{userId}";

                // Check cache first
                if (cache.TryGetValue(cacheKey, out List<OverdueNotificationDto>? cachedResult) && cachedResult != null)
                {
                    logger.LogInformation("[Notifications] Cache hit for user={User}, count={Count}", userId, cachedResult.Count);
                    return Results.Json(new { s = true, notifications = cachedResult });
                }

                // Get all companies the user has access to
                var accessResult = await access.NormalizeAndAuthorizeAsync(
                    user,
                    Array.Empty<string>(),
                    ct);

                if (!accessResult.Success)
                    return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

                var keys = accessResult.AccurateCompanyKeys;
                var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);

                logger.LogInformation(
                    "[Notifications] Computing overdue notifications for user={User}, companies={Companies}",
                    userId,
                    string.Join(", ", keys));

                var allNotifications = new List<OverdueNotificationDto>();

                foreach (var companyKey in keys)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        // Piutang (AR) notifications
                        var arNotifs = await OverdueNotificationComputation.ComputePiutangForCompany(
                            companyKey, asOfDate, accurateClient, ct);
                        allNotifications.AddRange(arNotifs);

                        // Utang (AP) notifications
                        var apNotifs = await OverdueNotificationComputation.ComputeUtangForCompany(
                            companyKey, asOfDate, accurateService, ct);
                        allNotifications.AddRange(apNotifs);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "[Notifications] Failed to compute notifications for company={Company}",
                            companyKey);
                        // Continue with other companies — don't fail the whole request
                    }
                }

                // Sort: highest severity first, then by entity name
                allNotifications = allNotifications
                    .OrderByDescending(n => n.AgingBucket)
                    .ThenBy(n => n.Type)
                    .ThenBy(n => n.EntityName)
                    .ToList();

                // Cache the result
                cache.Set(cacheKey, allNotifications, CacheTtl);

                logger.LogInformation(
                    "[Notifications] Computed {Count} overdue notifications for user={User}",
                    allNotifications.Count, userId);

                return Results.Json(new { s = true, notifications = allNotifications });
            })
            .RequireAuthorization()
            .WithTags("Notifications");

        app.MapPost("/api/notifications/email/test", async (
                SendTestEmailRequest request,
                IEmailService emailService,
                ILogger<SendTestEmailRequest> logger,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.ToEmail))
                    return Results.Json(new { error = "toEmail is required." }, statusCode: 400);

                try
                {
                    var htmlBody = request.UseTemplate
                        ? BuildDefaultEmailHtml(request)
                        : request.HtmlBody;

                    if (string.IsNullOrWhiteSpace(htmlBody))
                        return Results.Json(new { error = "htmlBody is required when useTemplate = false." }, statusCode: 400);

                    await emailService.SendAsync(new SendEmailRequest
                    {
                        ToEmail = request.ToEmail,
                        ToName = request.ToName,
                        Subject = request.Subject,
                        HtmlBody = htmlBody,
                        PlainTextBody = request.UseTemplate && string.IsNullOrWhiteSpace(request.PlainTextBody)
                            ? null
                            : request.PlainTextBody,
                    }, ct);

                    return Results.Json(new { s = true, message = "Email sent." });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send test email to {ToEmail}", request.ToEmail);
                    return Results.Json(new { error = ex.Message }, statusCode: 500);
                }
            })
            .RequireAuthorization()
            .WithTags("Notifications");

        app.MapPost("/api/notifications/email/pre-due", async (
                SendPreDueEmailRequest request,
                AccurateHttpClient accurateClient,
                IAccurateService accurateService,
                IEmailService emailService,
                IOptions<OverdueEmailScheduleOptions> scheduleOptions,
                IOptions<EmailOptions> emailOptions,
                ILogger<SendPreDueEmailRequest> logger,
                CancellationToken ct) =>
            {
                var schedule = scheduleOptions.Value;

                var reminderDays = (request.ReminderDaysBeforeDue?.Count > 0
                        ? request.ReminderDaysBeforeDue
                        : schedule.ReminderDaysBeforeDue)
                    .Where(x => x > 0)
                    .Distinct()
                    .OrderByDescending(x => x)
                    .ToList();

                if (reminderDays.Count == 0)
                {
                    return Results.Json(new
                    {
                        error = "ReminderDaysBeforeDue is empty. Set request.ReminderDaysBeforeDue or OverdueEmailSchedule:ReminderDaysBeforeDue."
                    }, statusCode: 400);
                }

                var recipients = (request.Recipients?.Count > 0
                        ? request.Recipients
                        : schedule.Recipients)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (recipients.Count == 0 && !string.IsNullOrWhiteSpace(emailOptions.Value.SenderEmail))
                {
                    recipients.Add(emailOptions.Value.SenderEmail);
                }

                if (recipients.Count == 0)
                {
                    return Results.Json(new
                    {
                        error = "No recipients configured. Set request.Recipients or OverdueEmailSchedule:Recipients."
                    }, statusCode: 400);
                }

                var companies = accurateClient.GetCompanyNames();
                if (companies.Count == 0)
                {
                    return Results.Json(new { error = "No Accurate companies configured." }, statusCode: 400);
                }

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var notifications = new List<OverdueNotificationDto>();

                if (request.TemplateOnly)
                {
                    var sampleReminderDay = reminderDays.FirstOrDefault();
                    if (sampleReminderDay <= 0)
                        sampleReminderDay = 1;

                    notifications.Add(new OverdueNotificationDto
                    {
                        Id = $"pre-due-sample-h{sampleReminderDay}",
                        Type = "utang",
                        Severity = sampleReminderDay == 1 ? "danger" : "warning",
                        Title = $"Utang Jatuh Tempo H-{sampleReminderDay} - SAMPLE",
                        Message = $"Template preview untuk H-{sampleReminderDay}.",
                        InvoiceNumber = "INV-SAMPLE-001",
                        TotalAmount = 15000000m,
                        EntityName = "PT SAMPLE ENTITAS",
                        CounterpartyName = "BANK SAMPLE",
                        DueDate = today.AddDays(sampleReminderDay).ToString("yyyy-MM-dd"),
                        DaysPastDue = -sampleReminderDay,
                        DaysUntilDue = sampleReminderDay,
                        AgingBucket = -sampleReminderDay,
                        CreatedAt = DateTime.UtcNow,
                        Category = "alert",
                    });
                }
                else
                {
                    foreach (var companyKey in companies)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var companyNotifs = await OverdueNotificationComputation.ComputeUtangDueSoonForCompany(
                                companyKey,
                                today,
                                accurateService,
                                reminderDays,
                                ct);

                            notifications.AddRange(companyNotifs);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "[Notifications] Failed computing pre-due utang notifications for company={Company}",
                                companyKey);
                        }
                    }
                }

                notifications = notifications
                    .OrderBy(x => x.DaysUntilDue ?? int.MaxValue)
                    .ThenBy(x => x.EntityName)
                    .ThenBy(x => x.InvoiceNumber)
                    .ToList();

                if (notifications.Count == 0)
                {
                    return Results.Json(new
                    {
                        s = true,
                        message = request.TemplateOnly
                            ? "Template preview sent."
                            : "No invoice matched configured pre-due reminder days.",
                        reminderDays,
                        recipients,
                        sent = 0,
                        failed = 0,
                        invoices = 0,
                    });
                }

                var sent = 0;
                var failed = 0;
                var errors = new List<object>();

                foreach (var to in recipients)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var item in notifications)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            await emailService.SendAsync(new SendEmailRequest
                            {
                                ToEmail = to,
                                Subject = OverdueUtangEmailTemplate.BuildSubject(item, item.DaysUntilDue),
                                HtmlBody = OverdueUtangEmailTemplate.BuildHtml(item, item.DaysUntilDue),
                                PlainTextBody = request.TemplateOnly ? null : OverdueUtangEmailTemplate.BuildPlainText(item, item.DaysUntilDue),
                            }, ct);
                            sent++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            logger.LogError(ex,
                                "[Notifications] Failed sending pre-due utang email to {Recipient} for invoice={Invoice}",
                                to,
                                item.InvoiceNumber);
                            errors.Add(new
                            {
                                to,
                                invoice = item.InvoiceNumber,
                                entity = item.EntityName,
                                hMinus = item.DaysUntilDue,
                                error = ex.Message,
                            });
                        }
                    }
                }

                return Results.Json(new
                {
                    s = failed == 0,
                    message = request.TemplateOnly
                        ? (failed == 0 ? "Template preview email sent." : "Template preview email sent with partial failures.")
                        : (failed == 0 ? "Pre-due emails sent." : "Pre-due emails sent with partial failures."),
                    reminderDays,
                    recipients,
                    invoices = notifications.Count,
                    sent,
                    failed,
                    errors,
                });
            })
            .RequireAuthorization()
            .WithTags("Notifications");

        app.MapPost("/api/notifications/email/overdue", async (
                SendOverdueEmailRequest request,
                AccurateHttpClient accurateClient,
                IAccurateService accurateService,
                IEmailService emailService,
                IOptions<OverdueEmailScheduleOptions> scheduleOptions,
                IOptions<EmailOptions> emailOptions,
                ILogger<SendOverdueEmailRequest> logger,
                CancellationToken ct) =>
            {
                var minimumDays = request.MinimumDaysPastDue <= 0 ? 30 : request.MinimumDaysPastDue;
                var schedule = scheduleOptions.Value;

                var recipients = (request.Recipients?.Count > 0
                        ? request.Recipients
                        : schedule.Recipients)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (recipients.Count == 0 && !string.IsNullOrWhiteSpace(emailOptions.Value.SenderEmail))
                {
                    recipients.Add(emailOptions.Value.SenderEmail);
                }

                if (recipients.Count == 0)
                {
                    return Results.Json(new
                    {
                        error = "No recipients configured. Set request.Recipients or OverdueEmailSchedule:Recipients."
                    }, statusCode: 400);
                }

                var companies = accurateClient.GetCompanyNames();
                if (companies.Count == 0)
                {
                    return Results.Json(new { error = "No Accurate companies configured." }, statusCode: 400);
                }

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var notifications = new List<OverdueNotificationDto>();

                if (request.TemplateOnly)
                {
                    notifications.Add(new OverdueNotificationDto
                    {
                        Id = "overdue-sample-hplus30",
                        Type = "utang",
                        Severity = "warning",
                        Title = "Utang Overdue H+30 - SAMPLE",
                        Message = "Template preview overdue 30+.",
                        InvoiceNumber = "INV-OD-001",
                        TotalAmount = 15000000m,
                        EntityName = "PT SAMPLE ENTITAS",
                        CounterpartyName = "BANK SAMPLE",
                        DueDate = today.AddDays(-minimumDays).ToString("yyyy-MM-dd"),
                        DaysPastDue = minimumDays,
                        DaysUntilDue = null,
                        AgingBucket = minimumDays,
                        CreatedAt = DateTime.UtcNow,
                        Category = "alert",
                    });
                }
                else
                {
                    foreach (var companyKey in companies)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var companyNotifs = await OverdueNotificationComputation.ComputeUtangOverdueForCompany(
                                companyKey,
                                today,
                                accurateService,
                                minimumDays,
                                ct);

                            notifications.AddRange(companyNotifs);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "[Notifications] Failed computing overdue utang notifications for company={Company}",
                                companyKey);
                        }
                    }
                }

                notifications = notifications
                    .OrderByDescending(x => x.DaysPastDue)
                    .ThenBy(x => x.EntityName)
                    .ThenBy(x => x.InvoiceNumber)
                    .ToList();

                if (notifications.Count == 0)
                {
                    return Results.Json(new
                    {
                        s = true,
                        message = request.TemplateOnly
                            ? "Template preview sent."
                            : "No invoice matched configured overdue days.",
                        minimumDaysPastDue = minimumDays,
                        recipients,
                        sent = 0,
                        failed = 0,
                        invoices = 0,
                    });
                }

                var sent = 0;
                var failed = 0;
                var errors = new List<object>();

                foreach (var to in recipients)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var item in notifications)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            await emailService.SendAsync(new SendEmailRequest
                            {
                                ToEmail = to,
                                Subject = OverdueUtangEmailTemplate.BuildOverdueSubject(item),
                                HtmlBody = OverdueUtangEmailTemplate.BuildOverdueHtml(item),
                                PlainTextBody = request.TemplateOnly ? null : OverdueUtangEmailTemplate.BuildOverduePlainText(item),
                            }, ct);
                            sent++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            logger.LogError(ex,
                                "[Notifications] Failed sending overdue utang email to {Recipient} for invoice={Invoice}",
                                to,
                                item.InvoiceNumber);
                            errors.Add(new
                            {
                                to,
                                invoice = item.InvoiceNumber,
                                entity = item.EntityName,
                                daysPastDue = item.DaysPastDue,
                                error = ex.Message,
                            });
                        }
                    }
                }

                return Results.Json(new
                {
                    s = failed == 0,
                    message = request.TemplateOnly
                        ? (failed == 0 ? "Template preview email sent." : "Template preview email sent with partial failures.")
                        : (failed == 0 ? "Overdue emails sent." : "Overdue emails sent with partial failures."),
                    minimumDaysPastDue = minimumDays,
                    recipients,
                    invoices = notifications.Count,
                    sent,
                    failed,
                    errors,
                });
            })
            .RequireAuthorization()
            .WithTags("Notifications");

        app.MapPost("/api/notifications/email/procurement", async (
                SendProcurementEmailRequest request,
                IEmailService emailService,
                IOptions<OverdueEmailScheduleOptions> scheduleOptions,
                IOptions<EmailOptions> emailOptions,
                ILogger<SendProcurementEmailRequest> logger,
                CancellationToken ct) =>
            {
                var schedule = scheduleOptions.Value;
                var recipients = (request.Recipients?.Count > 0
                        ? request.Recipients
                        : schedule.Recipients)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (recipients.Count == 0 && !string.IsNullOrWhiteSpace(emailOptions.Value.SenderEmail))
                {
                    recipients.Add(emailOptions.Value.SenderEmail);
                }

                if (recipients.Count == 0)
                {
                    return Results.Json(new
                    {
                        error = "No recipients configured. Set request.Recipients or OverdueEmailSchedule:Recipients."
                    }, statusCode: 400);
                }

                var sent = 0;
                var failed = 0;
                var errors = new List<object>();
                var htmlBody = BuildProcurementEmailHtml(request);

                foreach (var to in recipients)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await emailService.SendAsync(new SendEmailRequest
                        {
                            ToEmail = to,
                            Subject = request.Subject,
                            HtmlBody = htmlBody,
                            PlainTextBody = null,
                        }, ct);
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        logger.LogError(ex,
                            "[Notifications] Failed sending procurement email to {Recipient} for PR={PrNo}",
                            to,
                            request.NoPurchaseRequest);
                        errors.Add(new
                        {
                            to,
                            purchaseRequest = request.NoPurchaseRequest,
                            error = ex.Message,
                        });
                    }
                }

                return Results.Json(new
                {
                    s = failed == 0,
                    message = failed == 0 ? "Procurement emails sent." : "Procurement emails sent with partial failures.",
                    recipients,
                    sent,
                    failed,
                    errors,
                });
            })
            .RequireAuthorization()
            .WithTags("Notifications");

        return app;
    }
}
