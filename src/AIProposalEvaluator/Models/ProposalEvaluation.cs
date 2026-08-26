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
