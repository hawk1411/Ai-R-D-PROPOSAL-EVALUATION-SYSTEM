using AIProposalEvaluator.Data;
using AIProposalEvaluator.Models;
using Microsoft.EntityFrameworkCore;

namespace AIProposalEvaluator.Services;

public interface IEvaluationOrchestrator
{
    Task<EvaluationResult> EvaluateAsync(IFormFile file, double budget, CancellationToken ct = default);
    Task<List<HistoryItem>> GetHistoryAsync(int limit = 10);
}
