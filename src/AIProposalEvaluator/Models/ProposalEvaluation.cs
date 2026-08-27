using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIProposalEvaluator.Models;

public class ProposalEvaluation
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(500)]
    public string Filename { get; set; } = string.Empty;

    public double Novelty { get; set; }
    public double Finance { get; set; }
    public double FinalScore { get; set; }

    [MaxLength(200)]
    public string Decision { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? ReportPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EvaluationRequest
{
    public IFormFile File { get; set; } = null!;
    public double Budget { get; set; }
}

public class EvaluationResult
{
    public string ProposalText { get; set; } = string.Empty;
    public double Novelty { get; set; }
    public double Finance { get; set; }
    public List<string> Violations { get; set; } = new();
    public List<SimilarProject> SimilarProjects { get; set; } = new();
    public double FinalScore { get; set; }
    public double Confidence { get; set; }
    public ConfidenceBand ConfidenceBand { get; set; } = new();
    public string Decision { get; set; } = string.Empty;
    public List<string> Explanation { get; set; } = new();
    public Dictionary<string, double> FeatureImportance { get; set; } = new();
    public string AiReportText { get; set; } = string.Empty;
    public ShapResult ShapValues { get; set; } = new();
    public string ReportUrl { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class SimilarProject
{
    public string Project { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class ConfidenceBand
{
    public double Mean { get; set; }
    public double Lower { get; set; }
    public double Upper { get; set; }
    public double Std { get; set; }
    public double Confidence { get; set; }
}

public class ShapResult
{
    public double Baseline { get; set; }
    public Dictionary<string, double> Contributions { get; set; } = new();
}

public class HistoryItem
{
    public string Filename { get; set; } = string.Empty;
    public double FinalScore { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public class ChatRequest
{
    public string Question { get; set; } = string.Empty;
    public string ProposalText { get; set; } = string.Empty;
    public double FinalScore { get; set; }
    public string Decision { get; set; } = string.Empty;
}

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
}
