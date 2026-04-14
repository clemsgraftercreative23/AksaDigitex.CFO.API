using System.Globalization;
using MyBackend.Features.Notifications;

namespace MyBackend.Application.Services;

public static class OverdueUtangEmailTemplate
{
    public static string BuildSubject(OverdueNotificationDto item, int? configuredHMinus = null)
    {
        var hMinus = ResolveHMinus(item, configuredHMinus);
    if (hMinus == 1)
      return $"DARURAT - Jatuh Tempo H-1 | {item.InvoiceNumber}";

    return $"PERINGATAN JATUH TEMPO: H-{hMinus} | {item.InvoiceNumber}";
    }

    public static string BuildHtml(OverdueNotificationDto item, int? configuredHMinus = null)
    {
        var hMinus = ResolveHMinus(item, configuredHMinus);
        var nowText = DateTime.Now.ToString("HH:mm 'WIB'", CultureInfo.InvariantCulture);
        var dueDateText = string.IsNullOrWhiteSpace(item.DueDate) ? "-" : item.DueDate;
        var entityName = string.IsNullOrWhiteSpace(item.EntityName) ? "-" : item.EntityName;
        var invoiceNumber = string.IsNullOrWhiteSpace(item.InvoiceNumber) ? "-" : item.InvoiceNumber;
        var counterparty = string.IsNullOrWhiteSpace(item.CounterpartyName) ? "-" : item.CounterpartyName;
        var nominal = FormatRupiah(item.TotalAmount);
        var fileName = $"Dokumen_Tagihan_{Sanitize(counterparty)}.pdf";
        var hLabel = hMinus == 1 ? "BESOK" : $"{hMinus} Hari";
        var dueDateLabel = hMinus == 1 ? $"{dueDateText} - BESOK" : dueDateText;

        var isH1 = hMinus == 1;
        var isH2 = hMinus == 2;
        var isH3 = hMinus == 3;
        var isH5 = hMinus == 5;

        var headerBg = isH1 ? "#dc2626" : (isH2 ? "#ea580c" : (isH5 ? "#2563eb" : "#d97706"));
        var primaryText = isH1 ? "#b91c1c" : (isH2 ? "#c2410c" : (isH5 ? "#1d4ed8" : "#a16207"));
        var softBg = isH1 ? "#fff5f5" : (isH5 ? "#eff6ff" : "#fffbeb");
        var softBorder = isH1 ? "#fecaca" : (isH5 ? "#93c5fd" : "#facc15");
        var headerTitle = isH1 ? "DARURAT - JATUH TEMPO: H-1" : $"PERINGATAN JATUH TEMPO: H-{hMinus}";
        var topIcon = isH1 ? "&#9679;" : (isH5 ? "&#9203;" : "&#9888;");
        var calloutTitle = isH1
          ? "DARURAT - Jatuh Tempo BESOK"
          : (isH2
            ? "SEGERA - Jatuh Tempo dalam 2 Hari"
            : (isH3
              ? "Perhatian - Jatuh Tempo dalam 3 Hari"
              : (isH5 ? "Pengingat Awal - Jatuh Tempo dalam 5 Hari" : $"Perhatian - Jatuh Tempo dalam {hMinus} Hari")));
        var calloutDesc = isH1
          ? "Pembayaran WAJIB dieksekusi hari ini. Keterlambatan akan dikenakan penalti dan berpengaruh pada reputasi kredit perusahaan."
          : (isH2
            ? "Proses pembayaran harus dimulai hari ini untuk menghindari keterlambatan."
            : (isH3
              ? "Segera koordinasikan dengan Treasury untuk alokasi dana pembayaran."
              : (isH5
                ? "Silakan siapkan dana dan lakukan verifikasi saldo Cash of Account."
                : "Lakukan persiapan pembayaran sesuai jadwal jatuh tempo.")));

        var introParagraph = isH1
          ? $"Ini adalah notifikasi darurat terakhir. Tagihan <strong>{Escape(entityName)}</strong> jatuh tempo <strong>BESOK</strong>. Pembayaran harus dieksekusi <strong>hari ini sebelum pukul 14:00 WIB</strong> untuk menjamin dana efektif di rekening penerima."
          : (isH2
            ? $"Tagihan <strong>{Escape(entityName)}</strong> akan jatuh tempo dalam <strong>2 hari kerja</strong>. Mengingat proses transfer perbankan membutuhkan waktu 1x24 jam, mohon proses pembayaran <strong>dimulai hari ini</strong> tanpa penundaan."
            : (isH3
              ? $"Tagihan dari <strong>{Escape(entityName)}</strong> akan jatuh tempo dalam <strong>3 hari kerja</strong>. Mohon segera koordinasikan dengan tim Treasury untuk memastikan ketersediaan dana dan proses otorisasi pembayaran dapat diselesaikan tepat waktu."
              : (isH5
                ? $"Kami menginformasikan bahwa terdapat tagihan kepada <strong>{Escape(entityName)}</strong> yang akan jatuh tempo dalam <strong>5 hari kerja</strong>. Harap segera lakukan persiapan dana dan verifikasi ketersediaan saldo rekening."
                : $"Tagihan kepada <strong>{Escape(entityName)}</strong> akan jatuh tempo dalam <strong>{hMinus} hari</strong>. Mohon lakukan persiapan pembayaran sedini mungkin.")));

        var actionTitle = isH1
          ? "WAJIB SELESAI HARI INI"
          : (isH2 ? "TINDAKAN WAJIB HARI INI" : "CHECKLIST PERSIAPAN PEMBAYARAN");

        var actionItems = isH1
          ? new[]
          {
            "Eksekusi transfer sebelum 14:00 WIB (cut-off BI-FAST)",
            "Approval CFO &amp; Direktur Keuangan diperlukan segera",
            "Hubungi RM Bank jika ada kendala: {nama_rm_bank} - {no_rm_bank}",
            "Laporan status kepada manajemen paling lambat 15:00 WIB",
          }
          : (isH2
            ? new[]
            {
              "Eksekusi pembayaran via sistem e-banking sebelum pukul 15:00 WIB",
              "Kirim bukti transfer ke: finance@smartgarment.co.id",
              "CC kepada: Treasury, CFO, &amp; Relationship Manager Bank",
              "Update status di sistem AP Aging Dashboard",
            }
            : new[]
            {
              "Verifikasi saldo rekening giro yang akan digunakan",
              "Pastikan limit transaksi harian mencukupi",
              "Siapkan dokumen otorisasi pembayaran (2 approver)",
              "Konfirmasi ke bank jika diperlukan instruksi transfer",
            });

        var showActionBlock = !isH5;

        var actionItemsHtml = string.Join(string.Empty,
          actionItems.Select(x => $"<div style=\"margin:0 0 4px 0;\">&#8226; {x}</div>"));

        var estDendaRowHtml = (isH3 || isH5)
          ? string.Empty
          : """
            <tr>
              <td style="padding:7px 8px;color:#64748b;">Est. Denda Keterlambatan</td>
              <td style="padding:7px 8px;color:#dc2626;font-weight:700;">Rp {estimasi_denda} / hari</td>
            </tr>
    """;

        var actionBlockHtml = showActionBlock
            ? $$"""
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:14px;border:1px solid {{softBorder}};border-radius:6px;background:{{softBg}};overflow:hidden;">
                <tr>
                  <td style="padding:10px 10px 10px 10px;font-size:9px;line-height:1.5;color:{{primaryText}};">
                    <div style="font-weight:700;margin-bottom:4px;">&#9888; {{actionTitle}}</div>
{{actionItemsHtml}}
                  </td>
                </tr>
              </table>
"""
            : string.Empty;

        return $$"""
<!doctype html>
<html lang="id">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{{BuildSubject(item, hMinus)}}</title>
</head>
<body style="margin:0;padding:0;background:#eef2f7;font-family:Arial,Helvetica,sans-serif;color:#334155;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="padding:14px 0;background:#eef2f7;">
    <tr>
      <td align="center">
        <table role="presentation" width="380" cellpadding="0" cellspacing="0" style="width:380px;max-width:380px;background:#ffffff;border:1px solid #e5e7eb;border-radius:10px;overflow:hidden;box-shadow:0 4px 18px rgba(15,23,42,0.08);">
          <tr>
            <td style="background:{{headerBg}};color:#ffffff;padding:10px 16px;font-size:11px;font-weight:700;letter-spacing:0.3px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td style="font-size:11px;font-weight:700;letter-spacing:0.3px;">{{topIcon}} {{headerTitle}}</td>
                  <td align="right" style="font-size:10px;font-weight:700;">{{nowText}}</td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style="padding:18px 16px 12px 16px;">
              <div style="text-align:center;margin-bottom:16px;">
                <div style="width:34px;height:34px;margin:0 auto 10px auto;border-radius:10px;background:#fef2f2;color:{{headerBg}};line-height:34px;font-size:18px;">&#128188;</div>
                <div style="font-size:18px;line-height:1.2;font-weight:700;color:#0f172a;margin-bottom:4px;">Dashboard CFO</div>
                <div style="font-size:8px;line-height:1.3;color:#94a3b8;">Sistem Otomatisasi Pengingat Pembayaran (Bunga/Prioritas)</div>
              </div>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 14px 0;border:1px solid {{softBorder}};border-radius:6px;background:{{softBg}};overflow:hidden;">
                <tr>
                  <td style="padding:10px 10px 8px 10px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                      <tr>
                        <td valign="top" style="width:16px;padding-top:2px;">
                          <div style="width:12px;height:12px;border-radius:999px;background:{{headerBg}};display:inline-block;"></div>
                        </td>
                        <td>
                          <div style="font-size:10px;line-height:1.3;font-weight:700;color:{{primaryText}};">{{calloutTitle}}</div>
                          <div style="font-size:8px;line-height:1.35;color:{{primaryText}};">{{calloutDesc}}</div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>

              <div style="font-size:10px;line-height:1.6;color:#334155;margin-bottom:12px;">
                Yth. Tim Finance &amp; Direksi,<br><br>
                {{introParagraph}}
              </div>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #dbe4ef;border-radius:6px;overflow:hidden;font-size:9px;line-height:1.3;">
                <tr>
                  <td style="padding:7px 8px;width:42%;color:#64748b;border-bottom:1px solid #e8eef5;">Entitas</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(entityName)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">No. Invoice</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(invoiceNumber)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Jenis Tagihan</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">Utang</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Bank Terkait</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(counterparty)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Jatuh Tempo</td>
                  <td style="padding:7px 8px;color:{{headerBg}};font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(dueDateLabel)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Total Tagihan</td>
                  <td style="padding:7px 8px;color:{{headerBg}};font-weight:700;border-bottom:1px solid #e8eef5;">Rp {{Escape(nominal.Replace("Rp ", string.Empty, StringComparison.Ordinal))}}</td>
                </tr>
{{estDendaRowHtml}}
              </table>

{{actionBlockHtml}}

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:14px;border:1px dashed #cbd5e1;border-radius:8px;background:#ffffff;">
                <tr>
                  <td align="center" style="padding:14px 10px;">
                    <div style="font-size:22px;line-height:1;margin-bottom:8px;color:{{headerBg}};">&#128196;</div>
                    <div style="font-size:9px;font-weight:700;color:#0f172a;">{{Escape(fileName)}}</div>
                    <div style="font-size:7px;color:#94a3b8;margin-top:2px;">Ukuran file: 1.4 MB</div>
                    <a href="#" style="display:inline-block;margin-top:8px;background:{{headerBg}};color:#ffffff;text-decoration:none;padding:6px 16px;border-radius:5px;font-size:8px;font-weight:700;">DOWNLOAD PDF</a>
                  </td>
                </tr>
              </table>

              <div style="margin-top:10px;text-align:center;font-size:8px;line-height:1.4;color:#94a3b8;font-style:italic;">
                *Harap lakukan pengecekan saldo pada Cash of Account sebelum melakukan eksekusi pembayaran.
              </div>
            </td>
          </tr>
          <tr>
            <td style="padding:8px 16px 14px 16px;text-align:center;font-size:7px;line-height:1.5;color:#9ca3af;">
              Dokumen ini dihasilkan secara otomatis oleh Smart Garment Financial System.<br>
              &copy; 2026 Smart Garment Holding
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

    public static string BuildPlainText(OverdueNotificationDto item, int? configuredHMinus = null)
    {
        var hMinus = ResolveHMinus(item, configuredHMinus);
        var entityName = string.IsNullOrWhiteSpace(item.EntityName) ? "-" : item.EntityName;
        var invoiceNumber = string.IsNullOrWhiteSpace(item.InvoiceNumber) ? "-" : item.InvoiceNumber;
        var counterparty = string.IsNullOrWhiteSpace(item.CounterpartyName) ? "-" : item.CounterpartyName;
        var dueDate = string.IsNullOrWhiteSpace(item.DueDate) ? "-" : item.DueDate;

      var heading = hMinus == 1
        ? "Dashboard CFO - DARURAT Jatuh Tempo H-1"
        : $"Dashboard CFO - PERINGATAN Jatuh Tempo H-{hMinus}";

      var actionTitle = hMinus == 1
        ? "WAJIB SELESAI HARI INI"
        : (hMinus == 2 ? "TINDAKAN WAJIB HARI INI" : "CHECKLIST PERSIAPAN PEMBAYARAN");

      var actionItems = hMinus == 1
        ? new[]
        {
          "Eksekusi transfer sebelum 14:00 WIB (cut-off BI-FAST)",
          "Approval CFO & Direktur Keuangan diperlukan segera",
          "Hubungi RM Bank jika ada kendala",
          "Laporan status kepada manajemen paling lambat 15:00 WIB",
        }
        : (hMinus == 2
          ? new[]
          {
            "Eksekusi pembayaran via sistem e-banking sebelum pukul 15:00 WIB",
            "Kirim bukti transfer ke: finance@smartgarment.co.id",
            "CC kepada: Treasury, CFO, & Relationship Manager Bank",
            "Update status di sistem AP Aging Dashboard",
          }
          : new[]
          {
            "Verifikasi saldo rekening giro yang akan digunakan",
            "Pastikan limit transaksi harian mencukupi",
            "Siapkan dokumen otorisasi pembayaran (2 approver)",
            "Konfirmasi ke bank jika diperlukan instruksi transfer",
          });

      var estDenda = (hMinus == 3 || hMinus == 5) ? string.Empty : "Est. Denda Keterlambatan: Rp {estimasi_denda} / hari";
      var showActionBlock = hMinus != 5;

      var lines = new List<string>
      {
        heading,
            "",
        $"Entitas: {entityName}",
        $"No. Invoice: {invoiceNumber}",
            "Jenis Tagihan: Utang",
            $"Bank Terkait: {counterparty}",
            $"Jatuh Tempo: {dueDate}",
        $"Total Tagihan: {FormatRupiah(item.TotalAmount)}",
      };

      if (!string.IsNullOrWhiteSpace(estDenda))
      {
        lines.Add(estDenda);
      }

      if (showActionBlock)
      {
        lines.AddRange(
        [
          "",
          actionTitle,
        ]);

        lines.AddRange(actionItems.Select(x => $"- {x}"));
      }

      lines.AddRange(
      [
            "",
        "*Harap lakukan pengecekan saldo pada Cash of Account sebelum melakukan eksekusi pembayaran.",
      ]);

      return string.Join(Environment.NewLine, lines);
    }

    public static string BuildOverdueSubject(OverdueNotificationDto item)
    {
        var hPlus = item.DaysPastDue > 0 ? item.DaysPastDue : 30;
      if (hPlus >= 90)
        return $"MENUNGGAK {hPlus} HARI - STATUS NPL | {item.InvoiceNumber}";

        return hPlus >= 60
            ? $"MENUNGGAK {hPlus} HARI - TINDAKAN DIPERLUKAN | {item.InvoiceNumber}"
            : $"UTANG JATUH TEMPO - H+{hPlus} | {item.InvoiceNumber}";
    }

    public static string BuildOverdueHtml(OverdueNotificationDto item)
    {
        var nowText = DateTime.Now.ToString("HH:mm 'WIB'", CultureInfo.InvariantCulture);
        var hPlus = item.DaysPastDue > 0 ? item.DaysPastDue : 30;
      var is90Plus = hPlus >= 90;
        var is60Plus = hPlus >= 60;
        var entityName = string.IsNullOrWhiteSpace(item.EntityName) ? "-" : item.EntityName;
        var invoiceNumber = string.IsNullOrWhiteSpace(item.InvoiceNumber) ? "-" : item.InvoiceNumber;
        var counterparty = string.IsNullOrWhiteSpace(item.CounterpartyName) ? "-" : item.CounterpartyName;
        var dueDate = string.IsNullOrWhiteSpace(item.DueDate) ? "-" : item.DueDate;
        var nominal = FormatRupiah(item.TotalAmount);
        var totalKewajiban = FormatRupiah(item.TotalAmount);
      var fileName = is90Plus
        ? $"Laporan_NPL_AP_Aging_90hari_{Sanitize(counterparty)}.pdf"
        : (is60Plus
            ? $"Laporan_AP_Aging_60hari_{Sanitize(counterparty)}.pdf"
        : $"Laporan_AP_Aging_30hari_{Sanitize(counterparty)}.pdf");

      var headerBg = is90Plus ? "#0f172a" : (is60Plus ? "#be123c" : "#7c3aed");
      var calloutBg = is90Plus ? "#0b1736" : (is60Plus ? "#fff1f2" : "#f5f3ff");
      var calloutBorder = is90Plus ? "#334155" : (is60Plus ? "#fda4af" : "#c4b5fd");
      var calloutText = is90Plus ? "#f43f5e" : (is60Plus ? "#be123c" : "#6d28d9");
      var topIcon = is90Plus ? "&#9760;" : (is60Plus ? "&#9940;" : "&#9873;");
      var headerTitle = is90Plus
        ? $"MENUNGGAK {hPlus} HARI - STATUS NPL"
        : (is60Plus
            ? $"MENUNGGAK {hPlus} HARI - TINDAKAN DIPERLUKAN"
        : $"UTANG JATUH TEMPO - H+{hPlus}");
      var calloutTitle = is90Plus
        ? $"PERINGATAN KRITIS - {hPlus} Hari Menunggak (Ambang NPL)"
        : (is60Plus
            ? $"KRITIS - Invoice Belum Terbayar {hPlus} Hari"
        : $"Invoice Belum Terbayar - {hPlus} Hari Lewat Jatuh Tempo");
      var calloutDesc = is90Plus
        ? "Invoice ini sudah melewati 90 hari. Segera follow-up. Risiko masuk kategori Non-Performing Loan (NPL)."
        : (is60Plus
            ? "Risiko kredit meningkat signifikan. Diperlukan keputusan manajemen segera."
        : "Segera lakukan konfirmasi status pembayaran dan koordinasi dengan kreditur.");
      var introText = is90Plus
        ? $"Ini adalah notifikasi tingkat tertinggi. Tagihan <strong>{Escape(entityName)}</strong> senilai <strong>{Escape(nominal)}</strong> telah melewati <strong>90 hari</strong> sejak jatuh tempo (Invoice #{Escape(invoiceNumber)}). Berdasarkan regulasi OJK &amp; standar perbankan, utang ini berada di ambang klasifikasi <strong>Non-Performing Loan (NPL)</strong>, yang dapat berdampak serius terhadap fasilitas kredit seluruh entitas Smart Garment Group."
        : (is60Plus
            ? $"Tagihan dari <strong>{Escape(entityName)}</strong> telah menunggak selama <strong>{hPlus} hari</strong> sejak tanggal jatuh tempo. Kondisi ini telah masuk kategori <strong>\"Past Due - High Risk\"</strong> dalam sistem AP Aging. Diperlukan keputusan eskalasi dari manajemen puncak."
        : $"Terdapat tagihan dari <strong>{Escape(entityName)}</strong> yang telah melewati tanggal jatuh tempo <strong>{hPlus} hari</strong>. Hal ini dapat berdampak pada biaya denda, bunga keterlambatan, serta hubungan dengan mitra perbankan. Mohon segera lakukan tindak lanjut.");
      var actionTitle = is90Plus
        ? "TINDAKAN DARURAT - LEVEL DIREKSI"
        : (is60Plus
            ? "ESKALASI - KEPUTUSAN MANAJEMEN"
        : "FOLLOW-UP YANG DIPERLUKAN");
      var actionItems = is90Plus
        ? new[]
        {
          "Rapat Direksi &amp; Komisaris harus diadakan dalam 24 jam",
          "Hubungi Pimpinan Bank secara langsung: {nama_rm_bank} - {no_rm_bank}",
          "Siapkan proposal restrukturisasi / cicilan darurat resmi",
          "Evaluasi aset jaminan yang dapat digunakan sebagai pelunasan",
          "Konsultasi dengan tim Legal untuk mitigasi risiko hukum",
          "Notifikasi kepada Auditor Eksternal &amp; Komite Audit",
        }
        : (is60Plus
            ? new[]
            {
                "Jadwalkan meeting darurat dengan CFO, Direktur &amp; RM Bank",
                "Evaluasi opsi: pembayaran bertahap / restrukturisasi utang",
                "Siapkan surat permohonan penundaan resmi ke bank jika diperlukan",
                "Review dampak terhadap fasilitas kredit lain (cross-default)",
                "Update Dewan Direksi &amp; Dewan Komisaris tentang status ini",
            }
        : new[]
            {
                "Hubungi RM Bank: {nama_rm_bank} - {no_rm_bank}",
                "Negosiasikan restrukturisasi jika diperlukan",
                "Update status di AP Aging Dashboard",
                "Laporkan ke CFO &amp; Direktur Keuangan",
        });
        var actionItemsHtml = string.Join(string.Empty,
            actionItems.Select(x => $"<div>&#8226; {x}</div>"));
      var statusKreditRow = is90Plus
        ? """
          <tr>
            <td style="padding:7px 8px;color:#64748b;">Klasifikasi Kredit</td>
            <td style="padding:7px 8px;color:#dc2626;font-weight:700;">&#9940; Kurang Lancar / Diragukan (NPL Threshold)</td>
          </tr>
          <tr>
            <td style="padding:7px 8px;color:#64748b;">Dampak Potensial</td>
            <td style="padding:7px 8px;color:#dc2626;font-weight:700;">Penurunan rating kredit, pembekuan fasilitas</td>
          </tr>
  """
        : (is60Plus
            ? """
                <tr>
                  <td style="padding:7px 8px;color:#64748b;">Status Kredit</td>
                  <td style="padding:7px 8px;color:#dc2626;font-weight:700;">&#9888; High Risk - Watchlist</td>
                </tr>
"""
        : string.Empty);
        var detailTableRows = is60Plus
            ? """
                <tr>
                  <td style="padding:7px 8px;width:42%;color:#64748b;border-bottom:1px solid #e8eef5;">Entitas</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(entityName)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Bank Terkait</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(counterparty)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Jenis Tagihan</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">Utang</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Pokok Utang</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(nominal)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Akumulasi Denda &amp; Bunga</td>
                  <td style="padding:7px 8px;color:#dc2626;font-weight:700;border-bottom:1px solid #e8eef5;">Rp {akumulasi_denda}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Total Kewajiban</td>
                  <td style="padding:7px 8px;color:#dc2626;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(totalKewajiban)}}</td>
                </tr>
{{statusKreditRow}}
"""
            : """
                <tr>
                  <td style="padding:7px 8px;width:42%;color:#64748b;border-bottom:1px solid #e8eef5;">Entitas</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(entityName)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Bank Terkait</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">{{Escape(counterparty)}}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Jenis Tagihan</td>
                  <td style="padding:7px 8px;color:#1f2937;font-weight:700;border-bottom:1px solid #e8eef5;">Utang</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;border-bottom:1px solid #e8eef5;">Akumulasi Denda</td>
                  <td style="padding:7px 8px;color:#dc2626;font-weight:700;border-bottom:1px solid #e8eef5;">Rp {akumulasi_denda}</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;color:#64748b;">Total Kewajiban</td>
                  <td style="padding:7px 8px;color:#dc2626;font-weight:700;">{{Escape(totalKewajiban)}}</td>
                </tr>
""";
            var overdueCell = is90Plus
              ? $"{hPlus} hari &#9940;"
              : (is60Plus
              ? $"{hPlus} hari &#9888;"
            : $"{hPlus} hari");
            var closingNote = is90Plus
              ? "&#9888; Status ini bersifat RAHASIA MANAJEMEN. Distribusi terbatas pada Direksi, Komisaris, CFO, dan Legal."
              : (is60Plus
              ? "*Status ini telah dilaporkan ke sistem monitoring risiko kredit perbankan. Tindakan segera sangat diperlukan."
              : "*Invoice ini sudah melewati jatuh tempo. Setiap hari keterlambatan menambah akumulasi denda dan risiko kredit.");
            var footerLabel = is90Plus
              ? "&copy; 2026 Smart Garment Holding - Confidential"
              : "&copy; 2026 Smart Garment Holding";

        return $$"""
<!doctype html>
<html lang="id">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{{BuildOverdueSubject(item)}}</title>
</head>
<body style="margin:0;padding:0;background:#eef2f7;font-family:Arial,Helvetica,sans-serif;color:#334155;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="padding:14px 0;background:#eef2f7;">
    <tr>
      <td align="center">
        <table role="presentation" width="380" cellpadding="0" cellspacing="0" style="width:380px;max-width:380px;background:#ffffff;border:1px solid #e5e7eb;border-radius:10px;overflow:hidden;box-shadow:0 4px 18px rgba(15,23,42,0.08);">
          <tr>
            <td style="background:{{headerBg}};color:#ffffff;padding:10px 16px;font-size:11px;font-weight:700;letter-spacing:0.3px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td style="font-size:11px;font-weight:700;letter-spacing:0.3px;">{{topIcon}} {{headerTitle}}</td>
                  <td align="right" style="font-size:10px;font-weight:700;">{{nowText}}</td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style="padding:18px 16px 12px 16px;">
              <div style="text-align:center;margin-bottom:16px;">
                <div style="width:34px;height:34px;margin:0 auto 10px auto;border-radius:10px;background:#f3e8ff;color:{{headerBg}};line-height:34px;font-size:18px;">&#128188;</div>
                <div style="font-size:18px;line-height:1.2;font-weight:700;color:#0f172a;margin-bottom:4px;">Dashboard CFO - AP Aging</div>
                <div style="font-size:8px;line-height:1.3;color:#94a3b8;">Tagihan Lewat Jatuh Tempo - Smart Garment</div>
              </div>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 14px 0;border:1px solid {{calloutBorder}};border-radius:6px;background:{{calloutBg}};overflow:hidden;">
                <tr>
                  <td style="padding:10px 10px 8px 10px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                      <tr>
                        <td valign="top" style="width:16px;padding-top:2px;">
                          <div style="width:12px;height:12px;border-radius:999px;background:{{headerBg}};display:inline-block;"></div>
                        </td>
                        <td>
                          <div style="font-size:10px;line-height:1.3;font-weight:700;color:{{calloutText}};">{{calloutTitle}}</div>
                          <div style="font-size:8px;line-height:1.35;color:{{calloutText}};">{{calloutDesc}}</div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>

              <div style="font-size:10px;line-height:1.6;color:#334155;margin-bottom:12px;">
                Yth. Tim Finance &amp; Direksi,<br><br>
                {{introText}}
              </div>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;border:1px solid #dbe4ef;border-radius:6px;overflow:hidden;font-size:8px;line-height:1.3;">
                <tr style="background:#1e293b;color:#ffffff;">
                  <td style="padding:6px 8px;font-weight:700;border-right:1px solid #334155;">NO. INVOICE</td>
                  <td style="padding:6px 8px;font-weight:700;border-right:1px solid #334155;">JATUH TEMPO</td>
                  <td style="padding:6px 8px;font-weight:700;border-right:1px solid #334155;">UMUR (HARI)</td>
                  <td style="padding:6px 8px;font-weight:700;">NOMINAL</td>
                </tr>
                <tr>
                  <td style="padding:7px 8px;border-top:1px solid #e8eef5;">{{Escape(invoiceNumber)}}</td>
                  <td style="padding:7px 8px;border-top:1px solid #e8eef5;">{{Escape(dueDate)}}</td>
                  <td style="padding:7px 8px;border-top:1px solid #e8eef5;color:#dc2626;font-weight:700;">{{overdueCell}}</td>
                  <td style="padding:7px 8px;border-top:1px solid #e8eef5;font-weight:700;">{{Escape(nominal)}}</td>
                </tr>
              </table>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:10px;border:1px solid #dbe4ef;border-radius:6px;overflow:hidden;font-size:9px;line-height:1.3;">
{{detailTableRows}}
              </table>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:14px;border:1px solid {{calloutBorder}};border-radius:6px;background:{{calloutBg}};overflow:hidden;">
                <tr>
                  <td style="padding:10px 10px 10px 10px;font-size:9px;line-height:1.5;color:{{calloutText}};">
                    <div style="font-weight:700;margin-bottom:4px;">&#9888; {{actionTitle}}</div>
{{actionItemsHtml}}
                  </td>
                </tr>
              </table>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:14px;border:1px dashed #cbd5e1;border-radius:8px;background:#ffffff;">
                <tr>
                  <td align="center" style="padding:14px 10px;">
                    <div style="font-size:22px;line-height:1;margin-bottom:8px;color:#ef4444;">&#128196;</div>
                    <div style="font-size:9px;font-weight:700;color:#0f172a;">{{Escape(fileName)}}</div>
                    <div style="font-size:7px;color:#94a3b8;margin-top:2px;">Ukuran file: 1.4 MB</div>
                    <a href="#" style="display:inline-block;margin-top:8px;background:{{headerBg}};color:#ffffff;text-decoration:none;padding:6px 16px;border-radius:5px;font-size:8px;font-weight:700;">DOWNLOAD LAPORAN</a>
                  </td>
                </tr>
              </table>

              <div style="margin-top:10px;text-align:center;font-size:8px;line-height:1.4;color:#94a3b8;font-style:italic;">
                {{closingNote}}
              </div>
            </td>
          </tr>
          <tr>
            <td style="padding:8px 16px 14px 16px;text-align:center;font-size:7px;line-height:1.5;color:#9ca3af;">
              Dokumen ini dihasilkan secara otomatis oleh Smart Garment Financial System.<br>
              {{footerLabel}}
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

    public static string BuildOverduePlainText(OverdueNotificationDto item)
    {
        var hPlus = item.DaysPastDue > 0 ? item.DaysPastDue : 30;
      var is90Plus = hPlus >= 90;
      var is60Plus = hPlus >= 60;
        var entityName = string.IsNullOrWhiteSpace(item.EntityName) ? "-" : item.EntityName;
        var invoiceNumber = string.IsNullOrWhiteSpace(item.InvoiceNumber) ? "-" : item.InvoiceNumber;
        var counterparty = string.IsNullOrWhiteSpace(item.CounterpartyName) ? "-" : item.CounterpartyName;
        var dueDate = string.IsNullOrWhiteSpace(item.DueDate) ? "-" : item.DueDate;

      var heading = is90Plus
        ? $"Dashboard CFO - AP Aging NPL ALERT (H+{hPlus})"
        : (is60Plus
        ? $"Dashboard CFO - AP Aging KRITIS (H+{hPlus})"
        : $"Dashboard CFO - AP Aging (H+{hPlus})");
      var lead = is90Plus
        ? "Tagihan telah menunggak lebih dari 90 hari dan berisiko masuk klasifikasi NPL. Diperlukan tindakan level direksi segera."
        : (is60Plus
        ? "Kondisi telah masuk kategori Past Due - High Risk. Diperlukan keputusan eskalasi manajemen."
        : "Tagihan telah melewati jatuh tempo. Mohon segera tindak lanjut.");
      var actionTitle = is90Plus
        ? "TINDAKAN DARURAT - LEVEL DIREKSI"
        : (is60Plus
        ? "ESKALASI - KEPUTUSAN MANAJEMEN"
        : "FOLLOW-UP YANG DIPERLUKAN");
      var actionItems = is90Plus
        ? new[]
        {
          "Rapat Direksi & Komisaris harus diadakan dalam 24 jam",
          "Hubungi Pimpinan Bank secara langsung",
          "Siapkan proposal restrukturisasi / cicilan darurat resmi",
          "Evaluasi aset jaminan yang dapat digunakan sebagai pelunasan",
          "Konsultasi dengan tim Legal",
          "Notifikasi kepada Auditor Eksternal & Komite Audit",
        }
        : (is60Plus
        ? new[]
        {
          "Jadwalkan meeting darurat dengan CFO, Direktur & RM Bank",
          "Evaluasi opsi: pembayaran bertahap / restrukturisasi utang",
          "Siapkan surat permohonan penundaan resmi ke bank jika diperlukan",
          "Review dampak terhadap fasilitas kredit lain (cross-default)",
          "Update Dewan Direksi & Dewan Komisaris",
        }
        : new[]
        {
          "Hubungi RM Bank",
          "Negosiasikan restrukturisasi jika diperlukan",
          "Update status di AP Aging Dashboard",
          "Laporkan ke CFO & Direktur Keuangan",
        });

      var lines = new List<string>
      {
        heading,
            "",
            $"Entitas: {entityName}",
            $"No. Invoice: {invoiceNumber}",
            "Jenis Tagihan: Utang",
            $"Bank Terkait: {counterparty}",
            $"Jatuh Tempo: {dueDate}",
            $"Umur (Hari): {hPlus}",
            $"Nominal: {FormatRupiah(item.TotalAmount)}",
            "Akumulasi Denda: Rp {akumulasi_denda}",
      };

      if (is60Plus)
      {
        lines.Add("Status Kredit: High Risk - Watchlist");
      }

      if (is90Plus)
      {
        lines.Add("Klasifikasi Kredit: Kurang Lancar / Diragukan (NPL Threshold)");
        lines.Add("Dampak Potensial: Penurunan rating kredit, pembekuan fasilitas");
      }

      lines.AddRange(
      [
            "",
        lead,
        "",
        actionTitle,
      ]);

      lines.AddRange(actionItems.Select(x => $"- {x}"));
      return string.Join(Environment.NewLine, lines);
    }

    private static int ResolveHMinus(OverdueNotificationDto item, int? configuredHMinus)
    {
        if (item.DaysUntilDue is > 0)
            return item.DaysUntilDue.Value;

        if (configuredHMinus is > 0)
            return configuredHMinus.Value;

        return 1;
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "invoice";

        var invalid = Path.GetInvalidFileNameChars();
        return new string(input.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
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

    private static string FormatRupiah(decimal amount)
    {
        return string.Format(CultureInfo.InvariantCulture, "Rp {0:N0}", amount)
            .Replace(",", ".", StringComparison.Ordinal);
    }
}
