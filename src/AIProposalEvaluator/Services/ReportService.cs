using System.Globalization;
using AIProposalEvaluator.Models;
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AIProposalEvaluator.Services;

/// <summary>
/// Generates polished, multi-section PDF evaluation reports using QuestPDF and
/// persists them to the shared /reports directory so they can be served back
/// to the Blazor UI (or downloaded directly via a static file / minimal API route).
///
/// This service assumes the following shapes exist in AIProposalEvaluator.Models
/// (adjust property names here if your actual models differ slightly):
///
///   record EvaluationResult(
///       Guid Id, string ProposalTitle, string? ProposalId, DateTime EvaluatedAtUtc,
///       double FinalScore, string Decision,
///       NoveltyResult Novelty, FinancialResult Financial, MlEvaluationResult MlEvaluation,
///       List&lt;FeatureContribution&gt; FeatureContributions, string? GenAiNarrative);
///
///   record NoveltyResult(double Score, double MostSimilarScore, string? MostSimilarProjectTitle);
///
///   record FinancialResult(bool IsFeasible, double RequestedBudget, double RecommendedMax,
///       List&lt;string&gt; Flags);
///
///   record MlEvaluationResult(double PredictedScore, double ConfidenceLower,
///       double ConfidenceUpper, double UncertaintyStdDev);
///
///   record FeatureContribution(string FeatureName, double Contribution); // signed, SHAP-style
/// </summary>
public sealed class ReportService
{
    private readonly ILogger<ReportService> _logger;
    private readonly string _reportsDirectory;

    // Brand palette — tweak to match the Blazor UI's Bootstrap theme.
    private static readonly string ColorPrimary = Colors.Blue.Darken2;
    private static readonly string ColorPrimaryLight = Colors.Blue.Lighten4;
    private static readonly string ColorSuccess = Colors.Green.Darken1;
    private static readonly string ColorWarning = Colors.Orange.Darken1;
    private static readonly string ColorDanger = Colors.Red.Darken1;
    private static readonly string ColorMuted = Colors.Grey.Darken1;
    private static readonly string ColorBorder = Colors.Grey.Lighten2;

    static ReportService()
    {
        // Community license is free for most use cases (see QuestPDF docs for eligibility).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ReportService(ILogger<ReportService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _reportsDirectory = configuration["Reports:Directory"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "reports");

        Directory.CreateDirectory(_reportsDirectory);
    }

    /// <summary>
    /// Builds the full PDF report for a single evaluation and writes it to disk.
    /// Returns the absolute file path on disk plus the file name, so callers
    /// (e.g. the /api/submit endpoint) can construct a download URL.
    /// </summary>
    public async Task<ReportFile> GenerateReportAsync(EvaluationResult evaluation, CancellationToken ct = default)
    {
        var fileName = BuildFileName(evaluation);
        var fullPath = Path.Combine(_reportsDirectory, fileName);

        _logger.LogInformation("Generating PDF report for proposal '{Title}' -> {Path}",
            evaluation.ProposalTitle, fullPath);

        var document = BuildDocument(evaluation);

        // QuestPDF's generation is CPU-bound/synchronous; offload so we don't block the request thread.
        await Task.Run(() => document.GeneratePdf(fullPath), ct);

        return new ReportFile(fileName, fullPath, $"/reports/{fileName}");
    }

    /// <summary>Resolves a previously generated report's absolute path from its file name, or null if missing.</summary>
    public string? ResolveReportPath(string fileName)
    {
        var safeName = Path.GetFileName(fileName); // defend against path traversal
        var path = Path.Combine(_reportsDirectory, safeName);
        return File.Exists(path) ? path : null;
    }

    private string BuildFileName(EvaluationResult evaluation)
    {
        var slug = Sanitize(evaluation.ProposalTitle);
        var timestamp = evaluation.EvaluatedAtUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"report_{slug}_{timestamp}_{evaluation.Id:N}.pdf";
    }

    private static string Sanitize(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Where(c => !invalid.Contains(c)).ToArray())
            .Replace(' ', '_');
        return cleaned.Length == 0 ? "proposal" : (cleaned.Length > 40 ? cleaned[..40] : cleaned);
    }

    // ----------------------------------------------------------------------------
    // Document composition
    // ----------------------------------------------------------------------------

    private Document BuildDocument(EvaluationResult eval)
    {
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, eval));

                page.Content().Element(c => ComposeContent(c, eval));

                page.Footer().Element(ComposeFooter);
            });
        });
    }

    private void ComposeHeader(IContainer container, EvaluationResult eval)
    {
        container.PaddingBottom(12).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text("AI Proposal Evaluator").FontSize(16).Bold().FontColor(ColorPrimary);
                    inner.Item().Text("Automated R&D Proposal Assessment Report").FontSize(9).FontColor(ColorMuted);
                });

                row.ConstantItem(160).AlignRight().Column(inner =>
                {
                    inner.Item().AlignRight().Text(eval.EvaluatedAtUtc.ToString("MMM dd, yyyy HH:mm 'UTC'"))
                        .FontSize(9).FontColor(ColorMuted);
                    if (!string.IsNullOrWhiteSpace(eval.ProposalId))
                    {
                        inner.Item().AlignRight().Text($"Ref: {eval.ProposalId}").FontSize(9).FontColor(ColorMuted);
                    }
                });
            });

            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(ColorBorder);
        });
    }

    private void ComposeContent(IContainer container, EvaluationResult eval)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Spacing(16);

            col.Item().Element(c => ComposeTitleAndDecision(c, eval));
            col.Item().Element(c => ComposeScoreBreakdown(c, eval));
            col.Item().Element(c => ComposeFinancialSection(c, eval));
            col.Item().Element(c => ComposeFeatureContributions(c, eval));

            if (!string.IsNullOrWhiteSpace(eval.GenAiNarrative))
            {
                col.Item().Element(c => ComposeNarrative(c, eval));
            }
        });
    }

    private void ComposeTitleAndDecision(IContainer container, EvaluationResult eval)
    {
        var (label, color) = DecisionStyle(eval.Decision, eval.FinalScore);

        container.Column(col =>
        {
            col.Item().Text(eval.ProposalTitle).FontSize(18).Bold();

            col.Item().PaddingTop(8).Row(row =>
            {
                // Big score dial (simple circular-ish badge built from a rounded box).
                row.ConstantItem(110).Height(90).Background(ColorPrimaryLight).CornerRadius(6)
                    .AlignCenter().AlignMiddle().Column(scoreCol =>
                    {
                        scoreCol.Item().AlignCenter().Text(eval.FinalScore.ToString("0.0")).FontSize(28).Bold().FontColor(ColorPrimary);
                        scoreCol.Item().AlignCenter().Text("/ 100 FINAL SCORE").FontSize(7).FontColor(ColorMuted);
                    });

                row.ConstantItem(16);

                row.RelativeItem().Height(90).Background(Colors.Grey.Lighten5).CornerRadius(6)
                    .Padding(10).Column(decisionCol =>
                    {
                        decisionCol.Item().Text("DECISION").FontSize(8).FontColor(ColorMuted);
                        decisionCol.Item().PaddingTop(2).Text(label).FontSize(15).Bold().FontColor(color);
                        decisionCol.Item().PaddingTop(6).Text(t =>
                        {
                            t.Span("Thresholds: ").FontSize(8).FontColor(ColorMuted);
                            t.Span("≥85 Strongly Recommended · ≥70 Minor Revisions · <70 Not Recommended")
                                .FontSize(8).FontColor(ColorMuted);
                        });
                    });
            });
        });
    }

    private void ComposeScoreBreakdown(IContainer container, EvaluationResult eval)
    {
        container.Column(col =>
        {
            col.Item().Text("Score Breakdown").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(4).LineHorizontal(0.75f).LineColor(ColorBorder);

            var rows = new (string Label, double Value, string Detail)[]
            {
                ("Novelty", eval.Novelty.Score,
                    eval.Novelty.MostSimilarProjectTitle is { Length: > 0 }
                        ? $"Closest match: {eval.Novelty.MostSimilarProjectTitle} ({eval.Novelty.MostSimilarScore:0.0}% similar)"
                        : "No closely related historical project found"),
                ("Financial Feasibility", eval.Financial.IsFeasible ? 100 : 40,
                    $"Requested {eval.Financial.RequestedBudget:C0} vs recommended max {eval.Financial.RecommendedMax:C0}"),
                ("ML Ensemble Prediction", eval.MlEvaluation.PredictedScore,
                    $"90% confidence band: {eval.MlEvaluation.ConfidenceLower:0.0} – {eval.MlEvaluation.ConfidenceUpper:0.0} " +
                    $"(σ = {eval.MlEvaluation.UncertaintyStdDev:0.00})"),
            };

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(5);
                    c.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Metric");
                    HeaderCell(header, "Visual");
                    HeaderCell(header, "Score");
                });

                foreach (var row in rows)
                {
                    table.Cell().Element(BodyCellStyle).Text(row.Label).SemiBold();

                    table.Cell().Element(BodyCellStyle).Column(barCol =>
                    {
                        barCol.Item().Element(c => ComposeBar(c, row.Value));
                        barCol.Item().PaddingTop(2).Text(row.Detail).FontSize(7).FontColor(ColorMuted);
                    });

                    table.Cell().Element(BodyCellStyle).AlignRight().Text($"{row.Value:0.0}").Bold();
                }
            });
        });
    }

    /// <summary>Draws a simple horizontal bar chart segment (0-100 scale) without external chart libraries.</summary>
    private void ComposeBar(IContainer container, double value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        var barColor = clamped >= 85 ? ColorSuccess : clamped >= 70 ? ColorWarning : ColorDanger;

        container.Height(14).Background(Colors.Grey.Lighten3).CornerRadius(3).Row(row =>
        {
            row.RelativeItem((float)clamped).Background(barColor).CornerRadius(3);
            row.RelativeItem((float)(100 - clamped));
        });
    }

    private void ComposeFinancialSection(IContainer container, EvaluationResult eval)
    {
        container.Column(col =>
        {
            col.Item().Text("Financial Feasibility").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(4).LineHorizontal(0.75f).LineColor(ColorBorder);

            col.Item().Row(row =>
            {
                row.RelativeItem().Element(BodyCellStyle).Column(c =>
                {
                    c.Item().Text("Requested Budget").FontSize(8).FontColor(ColorMuted);
                    c.Item().Text(eval.Financial.RequestedBudget.ToString("C0")).FontSize(13).Bold();
                });
                row.RelativeItem().Element(BodyCellStyle).Column(c =>
                {
                    c.Item().Text("Recommended Max").FontSize(8).FontColor(ColorMuted);
                    c.Item().Text(eval.Financial.RecommendedMax.ToString("C0")).FontSize(13).Bold();
                });
                row.RelativeItem().Element(BodyCellStyle).Column(c =>
                {
                    c.Item().Text("Status").FontSize(8).FontColor(ColorMuted);
                    c.Item().Text(eval.Financial.IsFeasible ? "Within guidelines" : "Flagged")
                        .FontSize(13).Bold().FontColor(eval.Financial.IsFeasible ? ColorSuccess : ColorDanger);
                });
            });

            if (eval.Financial.Flags.Count > 0)
            {
                col.Item().PaddingTop(6).Column(flagCol =>
                {
                    flagCol.Item().Text("Flags raised:").FontSize(9).Bold();
                    foreach (var flag in eval.Financial.Flags)
                    {
                        flagCol.Item().Text($"• {flag}").FontSize(9).FontColor(ColorDanger);
                    }
                });
            }
        });
    }

    private void ComposeFeatureContributions(IContainer container, EvaluationResult eval)
    {
        if (eval.FeatureContributions is not { Count: > 0 } contributions)
            return;

        var maxAbs = contributions.Max(f => Math.Abs(f.Contribution));
        if (maxAbs <= 0) maxAbs = 1;

        container.Column(col =>
        {
            col.Item().Text("Explainability — Feature Contributions").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(2).Text("SHAP-style contribution of each factor to the final score (positive = increases score).")
                .FontSize(8).FontColor(ColorMuted);
            col.Item().PaddingBottom(4).LineHorizontal(0.75f).LineColor(ColorBorder);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(6);
                    c.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Feature");
                    HeaderCell(header, "Impact");
                    HeaderCell(header, "Value");
                });

                foreach (var f in contributions.OrderByDescending(x => Math.Abs(x.Contribution)).Take(10))
                {
                    table.Cell().Element(BodyCellStyle).Text(f.FeatureName);
                    table.Cell().Element(BodyCellStyle).Element(c => ComposeSignedBar(c, f.Contribution, maxAbs));
                    table.Cell().Element(BodyCellStyle).AlignRight()
                        .Text((f.Contribution >= 0 ? "+" : "") + f.Contribution.ToString("0.00"))
                        .FontColor(f.Contribution >= 0 ? ColorSuccess : ColorDanger).Bold();
                }
            });
        });
    }

    /// <summary>Diverging bar centered at zero — positive contributions extend right (green), negative left (red).</summary>
    private void ComposeSignedBar(IContainer container, double value, double maxAbs)
    {
        var ratio = Math.Clamp(Math.Abs(value) / maxAbs, 0, 1);
        var isPositive = value >= 0;

        container.Height(14).Row(row =>
        {
            row.RelativeItem(1).Row(leftRow =>
            {
                leftRow.RelativeItem(1 - (float)(isPositive ? 0 : ratio));
                if (!isPositive)
                    leftRow.RelativeItem((float)ratio).Background(ColorDanger).CornerRadius(2);
            });

            row.ConstantItem(2).Background(ColorBorder);

            row.RelativeItem(1).Row(rightRow =>
            {
                if (isPositive)
                    rightRow.RelativeItem((float)ratio).Background(ColorSuccess).CornerRadius(2);
                rightRow.RelativeItem(1 - (float)(isPositive ? ratio : 0));
            });
        });
    }

    private void ComposeNarrative(IContainer container, EvaluationResult eval)
    {
        container.Column(col =>
        {
            col.Item().Text("AI-Generated Narrative Summary").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(4).LineHorizontal(0.75f).LineColor(ColorBorder);

            col.Item().Background(Colors.Grey.Lighten5).CornerRadius(6).Padding(12)
                .Text(eval.GenAiNarrative!).FontSize(9.5f).LineHeight(1.35f);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(8).Column(col =>
        {
            col.Item().LineHorizontal(0.75f).LineColor(ColorBorder);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("Generated by AI Proposal Evaluator — for internal review purposes only.")
                    .FontSize(7).FontColor(ColorMuted);

                row.ConstantItem(80).AlignRight().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(7).FontColor(ColorMuted);
                    t.Span(" / ").FontSize(7).FontColor(ColorMuted);
                    t.TotalPages().FontSize(7).FontColor(ColorMuted);
                });
            });
        });
    }

    // ----------------------------------------------------------------------------
    // Small styling helpers
    // ----------------------------------------------------------------------------

    private static void HeaderCell(TableCellDescriptor cell, string text) =>
        cell.Element(c => c.Background(ColorPrimary).Padding(6))
            .Text(text).FontColor(Colors.White).FontSize(9).Bold();

    private static IContainer BodyCellStyle(IContainer container) =>
        container.BorderBottom(0.75f).BorderColor(ColorBorder).Padding(6);

    private static (string Label, string Color) DecisionStyle(string decision, double score) => score switch
    {
        >= 85 => ("STRONGLY RECOMMENDED FOR FUNDING", ColorSuccess),
        >= 70 => ("RECOMMENDED WITH MINOR REVISIONS", ColorWarning),
        _ => ("NOT RECOMMENDED", ColorDanger),
    };
}

/// <summary>Metadata describing a generated report file, returned to callers/controllers.</summary>
public sealed record ReportFile(string FileName, string FullPath, string RelativeUrl);
