using System;
using System.Collections.Generic;

namespace frontend.Models
{
    public class ProposalEvaluationRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "Artificial Intelligence";
        public decimal? Budget { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public byte[]? FileContent { get; set; }
        public string PrincipalInvestigator { get; set; } = string.Empty;
        public string Institution { get; set; } = string.Empty;
    }

    public class DimensionalScore
    {
        public string Name { get; set; } = string.Empty; // e.g., Novelty, Feasibility, Financial Alignment
        public double Score { get; set; } // 0 - 100
        public double Weight { get; set; } // e.g., 0.4
        public string Description { get; set; } = string.Empty;
        public string StatusColor { get; set; } = "#10B981"; // HEX or CSS variable
    }

    public class ShapFeature
    {
        public string FeatureName { get; set; } = string.Empty;
        public double ImpactScore { get; set; } // e.g., +14.2 or -6.5
        public string Category { get; set; } = string.Empty; // Technical, Team, Financial, Methodology
        public string Description { get; set; } = string.Empty;
        public bool IsPositive => ImpactScore >= 0;
    }

    public class ConfidenceInterval
    {
        public double MeanScore { get; set; }
        public double StdDeviation { get; set; }
        public double LowerBound { get; set; }
        public double UpperBound { get; set; }
        public double ConfidenceLevelPercentage { get; set; } = 95.0;
        public string Interpretation { get; set; } = string.Empty;
    }

    public class GenAiNarrative
    {
        public string ExecutiveSummary { get; set; } = string.Empty;
        public List<string> KeyStrengths { get; set; } = new();
        public List<string> IdentifiedRisks { get; set; } = new();
        public string BudgetAssessment { get; set; } = string.Empty;
        public string FinalRecommendation { get; set; } = "Highly Recommended for Funding"; // Recommended, Conditional, Rejected
        public string AuditNotes { get; set; } = string.Empty;
    }

    public class EvaluationResult
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ProposalTitle { get; set; } = string.Empty;
        public string Category { get; set; } = "General R&D";
        public string PrincipalInvestigator { get; set; } = "Dr. Jane Doe";
        public string Institution { get; set; } = "Stanford University";
        public decimal? RequestedBudget { get; set; }
        public string FileName { get; set; } = "proposal.pdf";
        public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

        // Scores
        public double OverallScore { get; set; } // 0 - 100
        public string ScoreTier { get; set; } = "Tier 1 - High Potential"; // Tier 1, Tier 2, Tier 3

        public ConfidenceInterval Confidence { get; set; } = new();
        public List<DimensionalScore> DimensionalScores { get; set; } = new();
        public List<ShapFeature> ShapExplanations { get; set; } = new();
        public GenAiNarrative Narrative { get; set; } = new();

        public string ReportDownloadUrl { get; set; } = string.Empty;
        public bool IsMockData { get; set; } = false;
    }

    public class PipelineStageProgress
    {
        public int StageNumber { get; set; }
        public string StageName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsFailed { get; set; }
    }
}
