using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace frontend.Services;

public interface IReviewerChatService
{
    Task<string> AskAsync(string question, string proposalText, string evaluationSummary);
}

public class ReviewerChatService : IReviewerChatService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ReviewerChatService> _logger;

    public ReviewerChatService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<ReviewerChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<string> AskAsync(string question, string proposalText, string evaluationSummary)
    {
        var apiKey = _config["OpenRouter:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return GenerateFallbackAnswer(question, proposalText, evaluationSummary);
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/AI-Proposal-Evaluator-DotNet");
            client.DefaultRequestHeaders.Add("X-Title", "AI Proposal Evaluator .NET");

            var prompt = $@"You are an expert research proposal reviewer.

Proposal:
{proposalText[..Math.Min(1500, proposalText.Length)]}

Evaluation Summary:
{evaluationSummary}

User Question:
{question}

Provide:
1. A direct answer.
2. Why the proposal received this evaluation.
3. Practical suggestions for improvement.
4. Mention any risks if applicable.

Keep the response within 6-8 lines.";

            var body = new
            {
                model = "meta-llama/llama-3.1-8b-instruct",
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.4,
                max_tokens = 250
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                return GenerateFallbackAnswer(question, proposalText, evaluationSummary);
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(text)
                ? GenerateFallbackAnswer(question, proposalText, evaluationSummary)
                : text.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reviewer chat failed");
            return GenerateFallbackAnswer(question, proposalText, evaluationSummary);
        }
    }

    private static string GenerateFallbackAnswer(string question, string proposalContext, string evaluationSummary)
    {
        var q = question.ToLowerInvariant();

        // Parse evaluation summary for dynamic data
        var parts = ParseSummary(evaluationSummary);
        var contextParts = ParseContext(proposalContext);

        // --- Score / Why this score ---
        if (q.Contains("score") || q.Contains("why") || q.Contains("rating") || q.Contains("receive"))
        {
            return $@"📊 Score Analysis for ""{parts.Title}""

The proposal received an overall score of {parts.Score}/100 ({parts.Tier}).

This score is derived from a weighted ensemble of multiple evaluation dimensions:
• Novelty & Innovation — How unique the proposal is compared to existing research in the corpus.
• Financial Feasibility — Whether the requested budget ({contextParts.Budget}) aligns with standard funding guidelines.
• Technical Soundness — The rigor of the methodology, milestones, and deliverables.
• Narrative Quality — Structural completeness and clarity of writing.

{(parts.ScoreValue >= 85 ? "With a score above 85, this proposal is strongly recommended for funding. The core ideas demonstrate significant innovation and the budget appears well-justified." : parts.ScoreValue >= 70 ? "With a score between 70-85, this proposal is recommended with minor revisions. There are solid foundations but some areas could be strengthened." : "With a score below 70, this proposal is currently not recommended. Significant improvements are needed across multiple dimensions.")}

💡 To see exactly which features contributed most, check the SHAP Explanation chart on the evaluation details page.";
        }

        // --- Improve / Suggestions ---
        if (q.Contains("improve") || q.Contains("better") || q.Contains("suggestion") || q.Contains("strengthen") || q.Contains("enhance"))
        {
            var strengths = contextParts.Strengths;
            var risks = contextParts.Risks;

            return $@"🚀 Improvement Recommendations for ""{parts.Title}""

Based on the {parts.Score}/100 evaluation, here are targeted suggestions:

1. 🔬 Novelty & Differentiation
   → Explicitly cite and contrast against 3-5 closely related works.
   → Clearly articulate what makes your approach fundamentally different.

2. 💰 Financial Justification
   → Provide a line-item budget breakdown (personnel, equipment, travel, overhead).
   → Justify each cost against specific deliverables and milestones.
   → Current budget: {contextParts.Budget}

3. 📋 Methodology & Milestones
   → Add a Gantt chart or timeline with quarterly milestones.
   → Define measurable success criteria for each phase.
   → Include contingency plans for high-risk technical components.

4. ⚠️ Risk Mitigation
   → Address identified risks: {(risks.Length > 0 ? risks : "Add a formal risk register with probability/impact matrix.")}
   → Include fallback strategies for critical path items.

5. 📝 Narrative Structure
   → Ensure all required sections are present: Abstract, Introduction, Literature Review, Methodology, Expected Impact, Budget, Timeline.
   → Have the proposal reviewed by a domain expert for clarity.

{(parts.ScoreValue >= 85 ? "Since the score is already strong, focus on polishing details to maximize funding chances." : "Addressing these areas could significantly raise the score.")}";
        }

        // --- Risks ---
        if (q.Contains("risk") || q.Contains("weakness") || q.Contains("concern") || q.Contains("problem") || q.Contains("issue"))
        {
            var risks = contextParts.Risks;

            return $@"⚠️ Risk Assessment for ""{parts.Title}""

{(risks.Length > 0 ? $"The AI evaluation identified these risks:\n{risks}" : "The automated evaluation flagged the following general risk categories:")}

Key Risk Areas to Consider:

1. 🔴 Technical Risk
   → Novel approaches may face unexpected implementation challenges.
   → Dependency on unproven technologies or algorithms.
   → Scalability concerns from lab prototype to production.

2. 🟡 Financial Risk
   → Budget ({contextParts.Budget}) must account for potential scope creep.
   → Equipment cost overruns or personnel turnover.
   → Exchange rate fluctuations for international collaborations.

3. 🟠 Timeline Risk
   → Overly ambitious milestones may lead to delays.
   → Regulatory or ethics approvals can add unforeseen waiting periods.

4. 🔵 Market/Impact Risk
   → Competing research may reach similar conclusions first.
   → The real-world applicability may be limited without industry partnerships.

Recommendation: Add a formal risk register with mitigation strategies to strengthen the proposal.";
        }

        // --- Strengths ---
        if (q.Contains("strength") || q.Contains("good") || q.Contains("positive") || q.Contains("well"))
        {
            var strengths = contextParts.Strengths;

            return $@"✅ Strengths Analysis for ""{parts.Title}""

{(strengths.Length > 0 ? $"Key strengths identified:\n{strengths}" : "")}

The proposal demonstrates strong qualities in several areas:

• 🏆 Overall Score: {parts.Score}/100 — classified as {parts.Tier}
• 📚 The research topic in {contextParts.Category} addresses a relevant and timely challenge.
• 🏛️ Institutional backing from {contextParts.Institution} adds credibility.
• 👤 PI: {contextParts.PI} — Principal Investigator credentials support the feasibility.

{(parts.ScoreValue >= 85 ? "This is a top-tier proposal. The fundamentals are excellent — focus on fine-tuning for maximum impact." : parts.ScoreValue >= 70 ? "The proposal has a solid foundation with clear potential. Addressing the identified gaps will elevate it further." : "While some strengths exist, significant improvements are needed to reach competitive levels.")}";
        }

        // --- Budget / Financial ---
        if (q.Contains("budget") || q.Contains("financial") || q.Contains("money") || q.Contains("cost") || q.Contains("fund"))
        {
            return $@"💰 Financial Assessment for ""{parts.Title}""

Requested Budget: {contextParts.Budget}
Budget Assessment: {contextParts.BudgetAssessment}

Financial Evaluation Criteria:
• Budget falls within acceptable range for the {contextParts.Category} category.
• Alignment with standard funding guidelines (typically $100K - $5M for R&D grants).
• Cost-to-impact ratio assessment.

Recommendations for Budget Section:
1. Provide detailed line-item breakdown (personnel, equipment, materials, travel, overhead).
2. Justify each expense against specific deliverables.
3. Include cost contingency (typically 10-15% buffer).
4. Show value-for-money through expected outcomes per dollar spent.
5. Benchmark against similar funded projects in the domain.";
        }

        // --- Methodology ---
        if (q.Contains("method") || q.Contains("approach") || q.Contains("technique") || q.Contains("how"))
        {
            return $@"🔬 Methodology Review for ""{parts.Title}""

The methodology evaluation considers:

1. Research Design
   → Is the approach appropriate for the stated objectives?
   → Are there clear hypotheses or research questions?

2. Data & Validation
   → What datasets or experimental setups will be used?
   → How will results be validated and reproduced?

3. Innovation
   → Does the methodology introduce novel techniques?
   → How does it advance beyond current state-of-the-art?

4. Feasibility
   → Can the proposed work be completed within the timeline?
   → Are required resources (compute, data, equipment) available?

To improve: Add explicit experimental protocols, define validation metrics, and include preliminary results if available.";
        }

        // --- General / Default ---
        return $@"🤖 AI Reviewer Response for ""{parts.Title}""

Evaluation Summary:
• Score: {parts.Score}/100
• Classification: {parts.Tier}
• Recommendation: {parts.Recommendation}
• Category: {contextParts.Category}
• PI: {contextParts.PI}
• Institution: {contextParts.Institution}
• Budget: {contextParts.Budget}

{(contextParts.Strengths.Length > 0 ? $"Key Strengths: {contextParts.Strengths}" : "")}
{(contextParts.Risks.Length > 0 ? $"Identified Risks: {contextParts.Risks}" : "")}
{(contextParts.BudgetAssessment.Length > 0 ? $"Budget Assessment: {contextParts.BudgetAssessment}" : "")}

You can ask me more specific questions like:
• ""Why did this proposal receive this score?""
• ""How can I improve this proposal?""
• ""What are the main risks?""
• ""What are the strengths?""
• ""Tell me about the budget assessment""
• ""Review the methodology""";
    }

    private static (string Title, string Score, double ScoreValue, string Tier, string Recommendation) ParseSummary(string summary)
    {
        var title = ExtractBetween(summary, "Title: ", ",") ?? "Unknown";
        var score = ExtractBetween(summary, "Overall Score: ", "/100") ?? "0";
        var tier = ExtractBetween(summary, "Tier: ", ",") ?? "Unknown";
        var rec = ExtractAfter(summary, "Recommendation: ") ?? "N/A";
        double.TryParse(score, out var scoreVal);
        return (title, score, scoreVal, tier, rec);
    }

    private static (string Category, string PI, string Institution, string Budget, string Strengths, string Risks, string BudgetAssessment) ParseContext(string context)
    {
        var category = ExtractLine(context, "Category:") ?? "General R&D";
        var pi = ExtractLine(context, "PI:") ?? "Not specified";
        var institution = ExtractLine(context, "Institution:") ?? "Not specified";
        var budget = ExtractLine(context, "Budget:") ?? "Not specified";
        var strengths = ExtractLine(context, "Key Strengths:") ?? "";
        var risks = ExtractLine(context, "Identified Risks:") ?? "";
        var budgetAssessment = ExtractLine(context, "Budget Assessment:") ?? "Standard assessment applied";
        return (category, pi, institution, budget, strengths, risks, budgetAssessment);
    }

    private static string? ExtractBetween(string text, string start, string end)
    {
        var startIdx = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return null;
        startIdx += start.Length;
        var endIdx = text.IndexOf(end, startIdx, StringComparison.OrdinalIgnoreCase);
        return endIdx < 0 ? text[startIdx..].Trim() : text[startIdx..endIdx].Trim();
    }

    private static string? ExtractAfter(string text, string marker)
    {
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return text[(idx + marker.Length)..].Trim();
    }

    private static string? ExtractLine(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return trimmed[key.Length..].Trim();
        }
        return null;
    }
}
