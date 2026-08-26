using AIProposalEvaluator.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AIProposalEvaluator.Services;

public interface IReportService
{
    (string Filename, string FullPath) Generate(
        Dictionary<string, double> scores,
        string decision,
        List<string> explanation,
        string? aiNarrative,
        ConfidenceBand? confidenceData,
        List<SimilarProject>? similarProjects = null,
        List<string>? violations = null);
}

public class ReportService : IReportService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ReportService> _logger;

    public ReportService(IWebHostEnvironment env, ILogger<ReportService> logger)
    {
        _env = env;
        _logger = logger;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public (string Filename, string FullPath) Generate(
        Dictionary<string, double> scores,
        string decision,
        List<string> explanation,
        string? aiNarrative,
        ConfidenceBand? confidenceData,
        List<SimilarProject>? similarProjects = null,
        List<string>? violations = null)
    {
        var reportsDir = Path.Combine(_env.ContentRootPath, "reports");
        Directory.CreateDirectory(reportsDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var uid = Guid.NewGuid().ToString("N")[..6];
        var filename = $"{timestamp}_{uid}_evaluation.pdf";
        var fullPath = Path.Combine(reportsDir, filename);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Text("AI-Based R&D Proposal Evaluation Report")
                        .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().PaddingTop(4).Text($"Generated On: {DateTime.Now:dd-MM-yyyy HH:mm:ss}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Blue.Lighten2);
                });

                page.Content().PaddingVertical(20).Column(col =>
                {
                    // Scores
                    col.Item().Text("ML-Based Evaluation Scores").FontSize(14).Bold();
                    col.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Metric").Bold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Score").Bold();
                        });

                        void Row(string label, double value)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(label);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Text($"{value:F2}");
                        }

                        if (scores.TryGetValue("novelty", out var n)) Row("Novelty Score", n);
                        if (scores.TryGetValue("finance", out var f)) Row("Financial Compliance", f);
                        if (scores.TryGetValue("final_score", out var fs)) Row("Final AI Score", fs);
                    });

                    // Decision
                    col.Item().PaddingTop(20).Text("Funding Recommendation").FontSize(14).Bold();
                    col.Item().PaddingTop(6).Text(decision).FontSize(12);

                    // Similar projects
                    if (similarProjects is { Count: > 0 })
                    {
                        col.Item().PaddingTop(18).Text("Novelty Benchmarking (Similar Past Projects)").FontSize(14).Bold();
                        foreach (var p in similarProjects)
                        {
                            col.Item().PaddingTop(4).Text($"• {p.Project}  (Similarity: {p.Similarity:P1})");
                        }
                    }

                    // Violations
                    if (violations is { Count: > 0 })
                    {
                        col.Item().PaddingTop(18).Text("Financial Guideline Violations").FontSize(14).Bold().FontColor(Colors.Red.Medium);
                        foreach (var v in violations)
                        {
                            col.Item().PaddingTop(4).Text($"✗ {v}").FontColor(Colors.Red.Darken1);
                        }
                    }

                    // XAI
                    col.Item().PaddingTop(18).Text("Explainable AI Insights").FontSize(14).Bold();
                    foreach (var point in explanation)
                    {
                        col.Item().PaddingTop(4).Text($"• {point}");
                    }

                    // Confidence
                    if (confidenceData != null)
                    {
                        col.Item().PaddingTop(18).Text("Model Confidence & Risk").FontSize(14).Bold();
                        col.Item().PaddingTop(6).Text($"Final Score (Mean): {confidenceData.Mean:F2}");
                        col.Item().Text($"Confidence Interval: {confidenceData.Lower:F2} – {confidenceData.Upper:F2}");
                        col.Item().Text($"Model Confidence: {confidenceData.Confidence:F2}%");
                    }

                    // Narrative
                    if (!string.IsNullOrWhiteSpace(aiNarrative))
                    {
                        col.Item().PaddingTop(18).Text("AI-Generated Evaluation Narrative").FontSize(14).Bold();
                        col.Item().PaddingTop(8).Text(aiNarrative).LineHeight(1.3f);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("This report was automatically generated using Machine Learning, Explainable AI, and Generative AI models. Human review is recommended.")
                        .FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf(fullPath);

        _logger.LogInformation("Report generated: {Path}", fullPath);
        return (filename, fullPath);
    }
}
