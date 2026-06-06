using PakistanAccountingERP.Application.DTOs;

namespace PakistanAccountingERP.Application.Interfaces.Services;

public interface IFbrSubmissionService
{
    Task<FbrSubmissionResult> SubmitAsync(
        FbrSubmissionRequest request,
        string? fbrPostUrl,
        string? apiToken,
        CancellationToken cancellationToken = default);
}
