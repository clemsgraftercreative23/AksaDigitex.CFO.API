using System.Globalization;
using MyBackend.Features.Notifications;

namespace MyBackend.Application.Services;

public static class OverdueUtangEmailTemplate
{
    public static string BuildSubject(OverdueNotificationDto item)
    {
        return $"PERINGATAN JATUH TEMPO: H+{item.DaysPastDue} - {item.InvoiceNumber}";
    }

    public static string BuildHtml(OverdueNotificationDto item)
    {
        var nowText = DateTime.Now.ToString("HH:mm 'WIB'", CultureInfo.InvariantCulture);
        var dueDateText = string.IsNullOrWhiteSpace(item.DueDate) ? "-" : item.DueDate;
        var counterparty = string.IsNullOrWhiteSpace(item.CounterpartyName) ? "-" : item.CounterpartyName;
        var nominal = FormatRupiah(item.TotalAmount);
        var fileName = $"Dokumen_Tagihan_{Sanitize(item.InvoiceNumber)}.pdf";

        return $$"""
<!doctype html>
<html lang="id">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{{BuildSubject(item)}}</title>
</head>
<body style="margin:0;padding:0;background:#f0f2f4;font-family:Arial,Helvetica,sans-serif;color:#2d3748;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f0f2f4;padding:20px 0;">
    <tr>
      <td align="center">
        <table role="presentation" width="620" cellpadding="0" cellspacing="0" style="width:620px;max-width:620px;background:#ffffff;border:1px solid #d9dee5;">
          <tr>
            <td style="background:#d9534f;color:#ffffff;padding:14px 18px;font-weight:700;font-size:21px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td style="font-size:22px;font-weight:700;">PERINGATAN JATUH TEMPO: H+{{item.DaysPastDue}}</td>
                  <td align="right" style="font-size:20px;font-weight:700;">{{nowText}}</td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style="padding:34px 30px 18px 30px;">
              <h1 style="margin:0 0 10px 0;text-align:center;font-size:46px;line-height:1.2;color:#2c3e50;">Dashboard CFO</h1>
              <p style="margin:0 0 28px 0;text-align:center;font-size:31px;color:#9aa4af;">Sistem Otomatisasi Pembayaran (Bunga/Prioritas)</p>
              <p style="margin:0 0 22px 0;font-size:34px;line-height:1.4;">Yth. Tim Finance &amp; Direksi,</p>
              <p style="margin:0 0 26px 0;font-size:38px;line-height:1.5;">Kami menginformasikan bahwa terdapat dokumen tagihan masuk yang sudah melewati jatuh tempo. Mohon periksa rincian di bawah ini untuk tindak lanjut.</p>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #e1e5ea;border-radius:6px;overflow:hidden;">
                <tr>
                  <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;border-bottom:1px solid #e9edf2;">Entitas:</td>
                  <td style="padding:16px 18px;font-size:32px;font-weight:700;border-bottom:1px solid #e9edf2;color:#2d3a4a;">{{Escape(item.EntityName)}}</td>
                </tr>
                <tr>
                  <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;border-bottom:1px solid #e9edf2;">Jenis Tagihan:</td>
                  <td style="padding:16px 18px;font-size:32px;font-weight:700;border-bottom:1px solid #e9edf2;color:#2d3a4a;">Utang</td>
                </tr>
                <tr>
                  <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;border-bottom:1px solid #e9edf2;">Vendor:</td>
                  <td style="padding:16px 18px;font-size:32px;font-weight:700;border-bottom:1px solid #e9edf2;color:#2d3a4a;">{{Escape(counterparty)}}</td>
                </tr>
                <tr>
                  <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;border-bottom:1px solid #e9edf2;">Jatuh Tempo:</td>
                  <td style="padding:16px 18px;font-size:32px;font-weight:700;border-bottom:1px solid #e9edf2;color:#d9534f;">{{Escape(dueDateText)}}</td>
                </tr>
                <tr>
                  <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;">Total Tagihan:</td>
                  <td style="padding:16px 18px;font-size:37px;font-weight:800;color:#2d3a4a;">{{Escape(nominal)}}</td>
                </tr>
              </table>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:30px;border:2px dashed #d3d8df;border-radius:8px;background:#fafbfc;">
                <tr>
                  <td align="center" style="padding:36px 20px;">
                    <div style="font-size:60px;line-height:1;margin-bottom:14px;">📄</div>
                    <div style="font-size:35px;font-weight:800;color:#2d3a4a;">{{Escape(fileName)}}</div>
                    <div style="font-size:27px;color:#9aa4af;margin-top:8px;">Invoice: {{Escape(item.InvoiceNumber)}}</div>
                    <a href="#" style="display:inline-block;margin-top:22px;background:#e74c3c;color:#ffffff;text-decoration:none;padding:13px 30px;border-radius:6px;font-size:26px;font-weight:800;">DETAIL INVOICE</a>
                  </td>
                </tr>
              </table>

              <p style="margin:28px 0 0 0;text-align:center;font-size:30px;color:#9aa4af;font-style:italic;">*Harap lakukan pengecekan saldo pada Cash of Account sebelum melakukan eksekusi pembayaran.</p>
            </td>
          </tr>
          <tr>
            <td style="padding:18px 24px;border-top:1px solid #eceff3;text-align:center;font-size:24px;color:#a4acb6;">Dokumen ini dihasilkan secara otomatis oleh Smart Garment Financial System.</td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    public static string BuildPlainText(OverdueNotificationDto item)
    {
        var counterparty = string.IsNullOrWhiteSpace(item.CounterpartyName) ? "-" : item.CounterpartyName;
        var dueDate = string.IsNullOrWhiteSpace(item.DueDate) ? "-" : item.DueDate;

        return string.Join(Environment.NewLine,
            "Dashboard CFO - Peringatan Jatuh Tempo",
            "",
            $"Entitas: {item.EntityName}",
            "Jenis Tagihan: Utang",
            $"Vendor: {counterparty}",
            $"Jatuh Tempo: {dueDate}",
            $"Hari Lewat Tempo: {item.DaysPastDue}",
            $"Nominal: {FormatRupiah(item.TotalAmount)}",
            $"Invoice: {item.InvoiceNumber}");
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "invoice";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(input.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }

    private static string FormatRupiah(decimal amount)
    {
        return string.Format(CultureInfo.InvariantCulture, "Rp {0:N0}", amount).Replace(",", ".", StringComparison.Ordinal);
    }
}
