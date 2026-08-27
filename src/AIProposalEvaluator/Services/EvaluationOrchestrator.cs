using AIProposalEvaluator.Data;
using AIProposalEvaluator.Models;
using Microsoft.EntityFrameworkCore;

namespace AIProposalEvaluator.Services;

public interface IEvaluationOrchestrator
{
    Task<EvaluationResult> EvaluateAsync(IFormFile file, double budget, CancellationToken ct = default);
    Task<List<HistoryItem>> GetHistoryAsync(int limit = 10);
}

public class EvaluationOrchestrator : IEvaluationOrchestrator
{
    private readonly IDocumentParserService _parser;
    private readonly INoveltyService _novelty;
    private readonly IFinancialService _finance;
    private readonly IMlEvaluationService _ml;
    private readonly INarrativeService _narrative;
    private readonly IReportService _report;
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EvaluationOrchestrator> _logger;

    private const double MinBudget = 100_000;
    private const double MaxBudget = 500_000_000;
    private const double DefaultTechnicalScore = 80.0;

    public EvaluationOrchestrator(
        IDocumentParserService parser,
        INoveltyService novelty,
        IFinancialService finance,
        IMlEvaluationService ml,
        INarrativeService narrative,
        IReportService report,
        AppDbContext db,
        IWebHostEnvironment env,
        ILogger<EvaluationOrchestrator> logger)
    {
        _parser = parser;
        _novelty = novelty;
        _finance = finance;
        _ml = ml;
        _narrative = narrative;
        _report = report;
        _db = db;
        _env = env;
        _logger = logger;
    }

    public async Task<EvaluationResult> EvaluateAsync(IFormFile file, double budget, CancellationToken ct = default)
    {
        if (budget < MinBudget || budget > MaxBudget)
        {
            return new EvaluationResult
            {
                Error = $"Budget must be between ₹{MinBudget:N0} and ₹{MaxBudget:N0}"
            };
        }

        // Save upload
        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var fileId = Guid.NewGuid().ToString("N");
        var safeName = Path.GetFileName(file.FileName);
        var filePath = Path.Combine(uploadsDir, $"{fileId}_{safeName}");

        await using (var stream = File.Create(filePath))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Extract text
        var text = _parser.ExtractTextFromPdf(filePath);

        if (string.IsNullOrWhiteSpace(text) || text.Length < 300)
        {
            return new EvaluationResult
            {
                Error = "Uploaded PDF does not appear to be a valid research proposal (insufficient extractable text)."
            };
        }

        // Novelty
        var (noveltyScore, similarProjects) = _novelty.Analyze(text);

        // Finance
        var (financeScore, violations) = _finance.Check(budget);

        // ML + Uncertainty
        var predictions = _ml.EvaluateWithUncertainty(
            noveltyScore, financeScore, DefaultTechnicalScore, budget);

        var confidenceBand = _ml.EstimateConfidenceBand(predictions);
        var finalScore = confidenceBand.Mean;
        var confidence = confidenceBand.Confidence;

        // Decision
        string decision = finalScore switch
        {
            >= 85 => "Strongly Recommended for Funding",
            >= 70 => "Recommended with Minor Revisions",
            _ => "Not Recommended"
        };

        // Explainability
        var explanation = _ml.GenerateExplanation(noveltyScore, financeScore, DefaultTechnicalScore);
        var featureImportance = _ml.GetFeatureImportance();
        var shapValues = _ml.GetShapLikeValues(noveltyScore, financeScore, DefaultTechnicalScore, budget);

        // GenAI narrative
        var aiNarrative = await _narrative.GenerateAsync(text, noveltyScore, financeScore, finalScore, decision);

        // PDF Report
        var scores = new Dictionary<string, double>
        {
            ["novelty"] = noveltyScore,
            ["finance"] = financeScore,
            ["final_score"] = finalScore
        };

        var (reportFilename, reportPath) = _report.Generate(
            scores, decision, explanation, aiNarrative, confidenceBand,
            similarProjects, violations);

        // Persist
        var record = new ProposalEvaluation
        {
            Filename = safeName,
            Novelty = noveltyScore,
            Finance = financeScore,
            FinalScore = finalScore,
            Decision = decision,
            ReportPath = reportPath,
            CreatedAt = DateTime.UtcNow
        };

        _db.ProposalEvaluations.Add(record);
        await _db.SaveChangesAsync(ct);

        return new EvaluationResult
        {
            ProposalText = text,
            Novelty = noveltyScore,
            Finance = financeScore,
            Violations = violations,
            SimilarProjects = similarProjects,
            FinalScore = finalScore,
            Confidence = confidence,
            ConfidenceBand = confidenceBand,
            Decision = decision,
            Explanation = explanation,
            FeatureImportance = featureImportance,
            AiReportText = aiNarrative,
            ShapValues = shapValues,
            ReportUrl = $"/reports/{reportFilename}"
        };
    }

    public async Task<List<HistoryItem>> GetHistoryAsync(int limit = 10)
    {
        return await _db.ProposalEvaluations
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(r => new HistoryItem
            {
                Filename = r.Filename,
                FinalScore = r.FinalScore,
                Decision = r.Decision,
                CreatedAt = r.CreatedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm")
            })
            .ToListAsync();
    }
}
