using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AIProposalEvaluator.Services;

public interface INarrativeService
{
    Task<string> GenerateAsync(string proposalText, double novelty, double finance, double finalScore, string decision);
}

public class NarrativeService : INarrativeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<NarrativeService> _logger;

    public NarrativeService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<NarrativeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string proposalText, double novelty, double finance, double finalScore, string decision)
    {
        var apiKey = _config["OpenRouter:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return GenerateFallbackNarrative(novelty, finance, finalScore, decision);
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/AI-Proposal-Evaluator-DotNet");
            client.DefaultRequestHeaders.Add("X-Title", "AI Proposal Evaluator .NET");

            var prompt = $@"You are an expert research funding reviewer.

Proposal Summary:
{proposalText[..Math.Min(1500, proposalText.Length)]}

Evaluation Scores:
- Novelty Score: {novelty:F2}
- Finance Score: {finance:F2}
- Final Score: {finalScore:F2}

Decision: {decision}

Write a professional evaluation narrative in 8–10 lines.";

            var body = new
            {
                model = "deepseek/deepseek-chat-v3-0324",
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.4,
                max_tokens = 400
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenRouter call failed: {Status}", response.StatusCode);
                return GenerateFallbackNarrative(novelty, finance, finalScore, decision);
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(text)
                ? GenerateFallbackNarrative(novelty, finance, finalScore, decision)
                : text.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate GenAI narrative");
            return GenerateFallbackNarrative(novelty, finance, finalScore, decision);
        }
    }

    private static string GenerateFallbackNarrative(double novelty, double finance, double finalScore, string decision)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Overall, the proposal demonstrates ");
        sb.Append(novelty >= 75 ? "strong novelty " : novelty >= 55 ? "moderate novelty " : "limited novelty ");
        sb.Append("relative to existing research.");
        sb.AppendLine();
        sb.AppendLine();
        sb.Append(finance >= 80
            ? "The requested budget is well-aligned with the scope of work and presents acceptable financial risk. "
            : "The financial request warrants closer scrutiny as it sits toward the higher end of typical grant ranges. ");
        sb.AppendLine();
        sb.AppendLine();
        sb.Append($"The composite AI evaluation score is {finalScore:F1}/100. ");
        sb.AppendLine($"Based on the multi-factor analysis the system recommends: **{decision}**.");
        sb.AppendLine();
        sb.AppendLine("Human expert review is still advised before final funding decisions are taken.");
        return sb.ToString();
    }
}
