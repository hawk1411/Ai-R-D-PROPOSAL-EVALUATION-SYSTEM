using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using frontend.Models;

namespace frontend.Services
{
    public interface IEvaluationService
    {
        bool UseMockMode { get; set; }
        string ApiEndpoint { get; set; }
        event Action<PipelineStageProgress>? OnPipelineProgress;

        Task<List<EvaluationResult>> GetEvaluationsAsync();
        Task<EvaluationResult?> GetEvaluationByIdAsync(string id);
        Task<EvaluationResult> SubmitEvaluationAsync(ProposalEvaluationRequest request);
        Task<byte[]> DownloadPdfReportAsync(string evaluationId);
        Task<bool> TestApiConnectionAsync();
        Task DeleteEvaluationAsync(string id);
    }
}
