using System.Globalization;
using AIProposalEvaluator.Models;
using Microsoft.Extensions.Configuration;
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
/// Built against the real AIProposalEvaluator.Models.EvaluationResult
/// (ProposalText, Novelty, Finance, FinalScore, Decision, Confidence,
/// ConfidenceBand, Violations, SimilarProjects, FeatureImportance,
/// ShapValues, AiReportText).
/// </summary>
public sealed class ReportService
{
    private readonly ILogger<ReportService> _logger;
    private readonly string _reportsDirectory;

    private static readonly string ColorPrimary = Colors.Blue.Darken2;
    private static readonly string ColorSuccess = Colors.Green.Darken1;
    private static readonly string ColorWarning = Colors.Orange.Darken1;
    private static readonly string ColorDanger = Colors.Red.Darken1;
    private static readonly string ColorMuted = Colors.Grey.Darken1;
    private static readonly string ColorBorder = Colors.Grey.Lighten2;

    static ReportService()
    {
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
    /// </summary>
    /// <param name="evaluation">The completed evaluation result.</param>
    /// <param name="proposalTitle">Display title / filename for the proposal (EvaluationResult has no title field).</param>
    /// <param name="requestedBudget">Requested budget, if known (e.g. from the original EvaluationRequest).</param>
    public async Task<ReportFile> GenerateReportAsync(
        EvaluationResult evaluation,
        string proposalTitle,
        double? requestedBudget = null,
        CancellationToken ct = default)
    {
        var title = string.IsNullOrWhiteSpace(proposalTitle) ? "Untitled Proposal" : proposalTitle;
        var fileName = BuildFileName(title);
        var fullPath = Path.Combine(_reportsDirectory, fileName);

        _logger.LogInformation("Generating PDF report for proposal '{Title}' -> {Path}", title, fullPath);

        var document = BuildDocument(evaluation, title, requestedBudget);

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

    private static string BuildFileName(string title)
    {
        var slug = Sanitize(title);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"report_{slug}_{timestamp}_{suffix}.pdf";
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

    private Document BuildDocument(EvaluationResult eval, string title, double? requestedBudget)
    {
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeHeader(c, title));
                page.Content().Element(c => ComposeContent(c, eval, requestedBudget));
                page.Footer().Element(ComposeFooter);
            });
        });
    }

    private void ComposeHeader(IContainer container, string title)
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
                    inner.Item().AlignRight().Text(DateTime.UtcNow.ToString("MMM dd, yyyy HH:mm 'UTC'"))
                        .FontSize(9).FontColor(ColorMuted);
                });
            });

            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(ColorBorder);
        });
    }

    private void ComposeContent(IContainer container, EvaluationResult eval, double? requestedBudget)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Spacing(16);

            col.Item().Element(c => ComposeTitleAndDecision(c, eval, "Proposal Evaluation"));
            col.Item().Element(c => ComposeScoreBreakdown(c, eval));
            col.Item().Element(c => ComposeFinancialSection(c, eval, requestedBudget));

            if (eval.SimilarProjects is { Count: > 0 })
                col.Item().Element(c => ComposeSimilarProjects(c, eval));

            col.Item().Element(c => ComposeFeatureContributions(c, eval));

            if (eval.Violations is { Count: > 0 })
                col.Item().Element(c => ComposeViolations(c, eval));

            if (!string.IsNullOrWhiteSpace(eval.AiReportText))
                col.Item().Element(c => ComposeNarrative(c, eval));
        });
    }

    private void ComposeTitleAndDecision(IContainer container, EvaluationResult eval, string title)
    {
        var (label, color) = DecisionStyle(eval.Decision, eval.FinalScore);

        container.Column(col =>
        {
            col.Item().Text(title).FontSize(18).Bold();

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Background(color).CornerRadius(4).Padding(10).Column(c =>
                {
                    c.Item().Text(label).FontColor(Colors.White).Bold().FontSize(11);
                    c.Item().Text($"Decision: {eval.Decision}").FontColor(Colors.White).FontSize(9);
                });

                row.ConstantItem(12);

                row.ConstantItem(110).Background(Colors.Grey.Lighten4).CornerRadius(4).Padding(10)
                    .Column(c =>
                    {
                        c.Item().AlignCenter().Text("Final Score").FontSize(8).FontColor(ColorMuted);
                        c.Item().AlignCenter().Text($"{eval.FinalScore:0.0}").FontSize(20).Bold().FontColor(color);
                    });
            });
        });
    }

    private void ComposeScoreBreakdown(IContainer container, EvaluationResult eval)
    {
        var rows = new (string Label, double Value, string Detail)[]
        {
            ("Novelty", eval.Novelty, "Similarity vs. historical proposal corpus"),
            ("Financial Feasibility", eval.Finance, "Alignment with budget guidelines"),
            ("Model Confidence", eval.Confidence, "Ensemble prediction confidence"),
        };

        container.Column(col =>
        {
            col.Item().Text("Score Breakdown").FontSize(13).Bold().FontColor(ColorPrimary);
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
                    HeaderCell(header, "Dimension");
                    HeaderCell(header, "Score");
                    HeaderCell(header, "Value");
                });

                foreach (var row in rows)
                {
                    table.Cell().Element(BodyCellStyle).Text(row.Label);
                    table.Cell().Element(BodyCellStyle).Column(barCol =>
                    {
                        barCol.Item().Element(c => ComposeBar(c, row.Value));
                        barCol.Item().PaddingTop(2).Text(row.Detail).FontSize(7).FontColor(ColorMuted);
                    });
                    table.Cell().Element(BodyCellStyle).AlignRight().Text($"{row.Value:0.0}").Bold();
                }
            });

            if (eval.ConfidenceBand is { } band)
            {
                col.Item().PaddingTop(6).Text(
                    $"95% confidence band: {band.Lower:0.0} - {band.Upper:0.0}  (mean {band.Mean:0.0}, sigma {band.Std:0.00})")
                    .FontSize(8).FontColor(ColorMuted);
            }
        });
    }

    /// <summary>Draws a simple horizontal bar (0-100 scale) without external chart libraries.</summary>
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

    private void ComposeFinancialSection(IContainer container, EvaluationResult eval, double? requestedBudget)
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
                    c.Item().Text(requestedBudget is { } b ? b.ToString("C0") : "N/A").FontSize(13).Bold();
                });
                row.RelativeItem().Element(BodyCellStyle).Column(c =>
                {
                    c.Item().Text("Finance Score").FontSize(8).FontColor(ColorMuted);
                    c.Item().Text($"{eval.Finance:0.0} / 100").FontSize(13).Bold();
                });
                row.RelativeItem().Element(BodyCellStyle).Column(c =>
                {
                    c.Item().Text("Status").FontSize(8).FontColor(ColorMuted);
                    var feasible = eval.Finance >= 60;
                    c.Item().Text(feasible ? "Within guidelines" : "Flagged")
                        .FontSize(13).Bold().FontColor(feasible ? ColorSuccess : ColorDanger);
                });
            });
        });
    }

    private void ComposeSimilarProjects(IContainer container, EvaluationResult eval)
    {
        container.Column(col =>
        {
            col.Item().Text("Similar Historical Projects").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(4).LineHorizontal(0.75f).LineColor(ColorBorder);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(6);
                    c.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Project");
                    HeaderCell(header, "Similarity");
                });

                foreach (var p in eval.SimilarProjects.OrderByDescending(x => x.Similarity).Take(5))
                {
                    table.Cell().Element(BodyCellStyle).Text(p.Project);
                    table.Cell().Element(BodyCellStyle).AlignRight().Text($"{p.Similarity:P0}");
                }
            });
        });
    }

    private void ComposeFeatureContributions(IContainer container, EvaluationResult eval)
    {
        var contributions = (eval.ShapValues?.Contributions is { Count: > 0 } shap
                ? shap
                : eval.FeatureImportance)
            ?.Where(kv => kv.Value != 0)
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .Take(10)
            .ToList();

        if (contributions is not { Count: > 0 })
            return;

        var maxAbs = contributions.Max(kv => Math.Abs(kv.Value));
        if (maxAbs <= 0) maxAbs = 1;

        container.Column(col =>
        {
            col.Item().Text("Explainability - Feature Contributions").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(2).Text("Contribution of each factor to the final score (positive = increases score).")
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

                foreach (var kv in contributions)
                {
                    table.Cell().Element(BodyCellStyle).Text(kv.Key);
                    table.Cell().Element(BodyCellStyle).Element(c => ComposeSignedBar(c, kv.Value, maxAbs));
                    table.Cell().Element(BodyCellStyle).AlignRight()
                        .Text((kv.Value >= 0 ? "+" : "") + kv.Value.ToString("0.00"))
                        .FontColor(kv.Value >= 0 ? ColorSuccess : ColorDanger).Bold();
                }
            });
        });
    }

    /// <summary>Diverging bar centered at zero - positive contributions extend right (green), negative left (red).</summary>
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

    private void ComposeViolations(IContainer container, EvaluationResult eval)
    {
        container.Column(col =>
        {
            col.Item().Text("Flags & Violations").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(4).LineHorizontal(0.75f).LineColor(ColorBorder);

            foreach (var v in eval.Violations)
            {
                col.Item().Text($"- {v}").FontSize(9).FontColor(ColorDanger);
            }
        });
    }

    private void ComposeNarrative(IContainer container, EvaluationResult eval)
    {
        container.Column(col =>
        {
            col.Item().Text("AI-Generated Narrative Summary").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingBottom(4).LineHorizontal(0.75f).LineColor(ColorBorder);

            col.Item().Background(Colors.Grey.Lighten5).CornerRadius(6).Padding(12)
                .Text(eval.AiReportText).FontSize(9.5f).LineHeight(1.35f);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(8).Column(col =>
        {
            col.Item().LineHorizontal(0.75f).LineColor(ColorBorder);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("Generated by AI Proposal Evaluator - for internal review purposes only.")
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
