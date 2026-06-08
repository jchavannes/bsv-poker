using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BsvPoker.App;

/// <summary>
/// BIP270 — the BSV merchant payment protocol used by services such as Anypay and Centi. A payment URL (or a
/// QR / pay: URI) points at a payment-request endpoint that returns a JSON PaymentRequest listing the outputs
/// to pay (each an amount + a locking script). The wallet builds and broadcasts a transaction paying exactly
/// those outputs, then POSTs a BIP270 Payment back to the merchant's paymentUrl and reads the PaymentACK.
/// Only the merchant interaction is HTTP; the money itself is a real on-chain Bitcoin transaction.
/// </summary>
public static class Bip270
{
    public sealed record Output(long Amount, byte[] Script);
    public sealed record PaymentRequest(string Network, List<Output> Outputs, string Memo, string PaymentUrl, string? MerchantData, long Total);
    public sealed record Ack(bool Ok, string Memo, string Raw);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Extract the payment-request URL from a raw input: a bare URL, or the <c>r=</c> / <c>uri</c>
    /// parameter of a bitcoin:/pay:/web+bsv: URI (how a scanned QR or copied invoice carries it).</summary>
    public static string? ExtractRequestUrl(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return raw;
        int q = raw.IndexOf('?');
        if (q >= 0)
            foreach (var kv in raw[(q + 1)..].Split('&'))
            {
                var p = kv.Split('=', 2);
                if (p.Length == 2 && (p[0].Equals("r", StringComparison.OrdinalIgnoreCase) || p[0].Equals("uri", StringComparison.OrdinalIgnoreCase)))
                    return Uri.UnescapeDataString(p[1]);
            }
        if (raw.StartsWith("pay:", StringComparison.OrdinalIgnoreCase)) { var b = raw[4..]; if (b.StartsWith("//")) b = "https:" + b; if (b.StartsWith("http")) return b; }
        return null;
    }

    /// <summary>GET and parse a BIP270 PaymentRequest (JSON) from the merchant.</summary>
    public static async Task<PaymentRequest> FetchAsync(string requestUrl)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        req.Headers.Accept.ParseAdd("application/bitcoinsv-paymentrequest");
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var network = root.TryGetProperty("network", out var n) ? (n.GetString() ?? "bitcoin") : "bitcoin";
        var memo = root.TryGetProperty("memo", out var m) ? (m.GetString() ?? "") : "";
        var payUrl = root.TryGetProperty("paymentUrl", out var pu) ? (pu.GetString() ?? "") : "";
        string? merchant = root.TryGetProperty("merchantData", out var md) ? md.GetString() : null;
        var outs = new List<Output>();
        if (root.TryGetProperty("outputs", out var oArr) && oArr.ValueKind == JsonValueKind.Array)
            foreach (var o in oArr.EnumerateArray())
            {
                long amt = o.TryGetProperty("amount", out var a) ? a.GetInt64() : 0;
                var scriptHex = o.TryGetProperty("script", out var s) ? (s.GetString() ?? "") : "";
                outs.Add(new Output(amt, Convert.FromHexString(scriptHex)));
            }
        if (outs.Count == 0) throw new InvalidOperationException("payment request has no outputs");
        return new PaymentRequest(network, outs, memo, payUrl, merchant, outs.Sum(o => o.Amount));
    }

    /// <summary>POST the BIP270 Payment (the signed transaction hex + optional merchant data) and read the ACK.</summary>
    public static async Task<Ack> SubmitAsync(PaymentRequest pr, string rawTxHex, string? refundAddress, string memo)
    {
        if (string.IsNullOrWhiteSpace(pr.PaymentUrl)) return new Ack(true, "(no paymentUrl — broadcast only)", "");
        var payment = new Dictionary<string, object?>
        {
            ["transaction"] = rawTxHex,
            ["merchantData"] = pr.MerchantData,
            ["memo"] = memo,
        };
        if (!string.IsNullOrWhiteSpace(refundAddress))
            payment["refundTo"] = refundAddress;
        var body = new StringContent(JsonSerializer.Serialize(payment), Encoding.UTF8, "application/bitcoinsv-payment");
        using var resp = await Http.PostAsync(pr.PaymentUrl, body);
        var text = await resp.Content.ReadAsStringAsync();
        bool ok = resp.IsSuccessStatusCode;
        string ackMemo = "";
        try { using var doc = JsonDocument.Parse(text); if (doc.RootElement.TryGetProperty("memo", out var mm)) ackMemo = mm.GetString() ?? ""; } catch { }
        return new Ack(ok, ackMemo, text);
    }
}
