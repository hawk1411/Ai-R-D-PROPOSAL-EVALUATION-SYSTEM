using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AIProposalEvaluator.Services;

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
            return GenerateFallbackAnswer(question, evaluationSummary);
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
                return GenerateFallbackAnswer(question, evaluationSummary);
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(text)
                ? GenerateFallbackAnswer(question, evaluationSummary)
                : text.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reviewer chat failed");
            return GenerateFallbackAnswer(question, evaluationSummary);
        }
    }

    private static string GenerateFallbackAnswer(string question, string evaluationSummary)
    {
        return $@"Based on the evaluation ({evaluationSummary}):

The current scores reflect a balanced assessment of novelty, financial realism and technical feasibility. 
To improve the proposal you can: strengthen the differentiation from prior art, provide clearer milestones and risk mitigation, and ensure the budget is tightly justified against deliverables.

If you have a more specific question about a particular score component, please ask again.";
    }
}
