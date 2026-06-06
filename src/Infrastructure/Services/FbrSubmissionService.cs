using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;

namespace PakistanAccountingERP.Infrastructure.Services;

public class FbrSubmissionService : IFbrSubmissionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FbrSubmissionService> _logger;

    public FbrSubmissionService(
        IHttpClientFactory httpClientFactory,
        ILogger<FbrSubmissionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<FbrSubmissionResult> SubmitAsync(
        FbrSubmissionRequest request,
        string? fbrPostUrl,
        string? apiToken,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(request);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        if (string.IsNullOrWhiteSpace(fbrPostUrl) || string.IsNullOrWhiteSpace(apiToken))
        {
            var simulatedNumber = $"FBR-DEMO-{request.InvoiceNumber}";
            var simulatedResponse = JsonSerializer.Serialize(new
            {
                success = true,
                mode = "simulation",
                message = "FBR API URL or token not configured. Simulated submission stored.",
                fbrInvoiceNumber = simulatedNumber,
                submittedAt = DateTime.UtcNow,
                request = payload
            }, JsonOptions);

            return new FbrSubmissionResult(
                true,
                "Invoice submitted to FBR (simulation mode). Configure company FBR URL and API token for live submission.",
                simulatedNumber,
                simulatedResponse,
                true);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("FbrApi");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, fbrPostUrl.Trim());
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
            httpRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "FBR API returned {StatusCode} for invoice {InvoiceNumber}",
                    response.StatusCode,
                    request.InvoiceNumber);

                return new FbrSubmissionResult(
                    false,
                    $"FBR API error ({(int)response.StatusCode}): {Truncate(responseBody, 500)}",
                    null,
                    responseBody,
                    false);
            }

            var fbrNumber = TryExtractFbrInvoiceNumber(responseBody) ?? $"FBR-{request.InvoiceNumber}";
            return new FbrSubmissionResult(
                true,
                "Invoice submitted to FBR successfully.",
                fbrNumber,
                responseBody,
                false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FBR submission failed for invoice {InvoiceNumber}", request.InvoiceNumber);
            return new FbrSubmissionResult(false, $"FBR submission failed: {ex.Message}", null, null, false);
        }
    }

    private static object BuildPayload(FbrSubmissionRequest request) =>
        new
        {
            invoiceType = "Sale Invoice",
            invoiceNumber = request.InvoiceNumber,
            invoiceDate = request.InvoiceDate.ToString("yyyy-MM-dd"),
            sellerNTN = request.SellerNtn,
            buyerNTN = request.BuyerNtn,
            buyerCNIC = request.BuyerCnic,
            buyerName = request.BuyerName,
            scenarioId = request.ScenarioId,
            scenarioCode = request.ScenarioCode,
            subTotal = request.SubTotal,
            discount = request.DiscountAmount,
            salesTax = request.TaxAmount,
            totalAmount = request.NetTotal,
            items = request.Lines.Select(l => new
            {
                hsCode = l.HsCode,
                productDescription = l.ProductDescription,
                uom = l.Unit,
                quantity = l.Quantity,
                rate = l.Price,
                taxRate = l.TaxRate,
                salesTax = l.TaxAmount,
                total = l.LineTotal
            })
        };

    private static string? TryExtractFbrInvoiceNumber(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            foreach (var key in new[] { "fbrInvoiceNumber", "FbrInvoiceNumber", "invoiceNumber", "InvoiceNumber", "irn" })
            {
                if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // ignore parse errors; caller will fall back to generated number
        }

        return null;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength] + "...";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
