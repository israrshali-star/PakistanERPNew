using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
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
        var payloadJson = FbrPayloadBuilder.BuildJson(request);

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
                request = FbrPayloadBuilder.BuildObject(request)
            });

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

            var parsed = ParseFbrResponse(responseBody);
            if (!parsed.IsValid)
            {
                _logger.LogWarning(
                    "FBR validation failed for invoice {InvoiceNumber}: {Error}",
                    request.InvoiceNumber,
                    parsed.ErrorMessage);

                return new FbrSubmissionResult(
                    false,
                    parsed.ErrorMessage ?? "FBR rejected the invoice.",
                    null,
                    responseBody,
                    false);
            }

            if (string.IsNullOrWhiteSpace(parsed.InvoiceNumber))
            {
                return new FbrSubmissionResult(
                    false,
                    "FBR accepted the request but did not return an invoice number.",
                    null,
                    responseBody,
                    false);
            }

            return new FbrSubmissionResult(
                true,
                "Invoice submitted to FBR successfully.",
                parsed.InvoiceNumber,
                responseBody,
                false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FBR submission failed for invoice {InvoiceNumber}", request.InvoiceNumber);
            return new FbrSubmissionResult(false, $"FBR submission failed: {ex.Message}", null, null, false);
        }
    }

    private static FbrParseResult ParseFbrResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("validationResponse", out var validation)
                && validation.ValueKind == JsonValueKind.Object)
            {
                var statusCode = GetStringProp(validation, "statusCode");
                var status = GetStringProp(validation, "status");
                var isInvalid = string.Equals(statusCode, "01", StringComparison.Ordinal)
                    || string.Equals(status, "Invalid", StringComparison.OrdinalIgnoreCase);

                if (isInvalid)
                {
                    return new FbrParseResult(false, null, BuildValidationErrorMessage(validation));
                }
            }

            var invoiceNumber = TryGetInvoiceNumber(root);
            return new FbrParseResult(true, invoiceNumber, null);
        }
        catch (JsonException)
        {
            return new FbrParseResult(false, null, "FBR returned a non-JSON response.");
        }
    }

    private static string BuildValidationErrorMessage(JsonElement validation)
    {
        var parts = new List<string>();

        var topError = GetStringProp(validation, "error");
        var topErrorCode = GetStringProp(validation, "errorCode");
        if (!string.IsNullOrWhiteSpace(topError))
        {
            parts.Add(string.IsNullOrWhiteSpace(topErrorCode)
                ? topError
                : $"[{topErrorCode}] {topError}");
        }

        if (validation.TryGetProperty("invoiceStatuses", out var statuses)
            && statuses.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in statuses.EnumerateArray())
            {
                var itemStatus = GetStringProp(item, "status");
                var itemStatusCode = GetStringProp(item, "statusCode");
                var itemInvalid = string.Equals(itemStatusCode, "01", StringComparison.Ordinal)
                    || string.Equals(itemStatus, "Invalid", StringComparison.OrdinalIgnoreCase);
                if (!itemInvalid)
                {
                    continue;
                }

                var itemSNo = GetStringProp(item, "itemSNo") ?? "?";
                var itemError = GetStringProp(item, "error") ?? "Invalid line.";
                var itemErrorCode = GetStringProp(item, "errorCode");
                parts.Add(string.IsNullOrWhiteSpace(itemErrorCode)
                    ? $"Line {itemSNo}: {itemError}"
                    : $"Line {itemSNo} [{itemErrorCode}]: {itemError}");
            }
        }

        if (parts.Count == 0)
        {
            return "FBR validation failed (status Invalid). See response JSON for details.";
        }

        return "FBR validation failed. " + string.Join(" ", parts);
    }

    private static string? TryGetInvoiceNumber(JsonElement root)
    {
        foreach (var key in new[] { "invoiceNumber", "InvoiceNumber", "fbrInvoiceNumber", "FbrInvoiceNumber", "irn" })
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        if (root.TryGetProperty("validationResponse", out var validation)
            && validation.ValueKind == JsonValueKind.Object
            && validation.TryGetProperty("invoiceStatuses", out var statuses)
            && statuses.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in statuses.EnumerateArray())
            {
                if (item.TryGetProperty("invoiceNo", out var invoiceNo)
                    && invoiceNo.ValueKind == JsonValueKind.String)
                {
                    var text = invoiceNo.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }
        }

        return null;
    }

    private static string? GetStringProp(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength] + "...";
    }

    private sealed record FbrParseResult(bool IsValid, string? InvoiceNumber, string? ErrorMessage);
}
