namespace AIProposalEvaluator.Services;

public interface IFinancialService
{
    (double FinanceScore, List<string> Violations) Check(double budget);
}

public class FinancialService : IFinancialService
{
    // Align roughly with original limits (₹)
    private const double SoftMaxBudget = 5_000_000;   // 50 Lakh soft warning
    private const double HardMaxBudget = 500_000_000; // 50 Crore absolute
    private const double MinBudget = 100_000;         // 1 Lakh

    public (double FinanceScore, List<string> Violations) Check(double budget)
    {
        var violations = new List<string>();
        double score = 100.0;

        if (budget > HardMaxBudget)
        {
            violations.Add($"Budget exceeds absolute maximum allowed limit (₹{HardMaxBudget:N0}).");
            score -= 50;
        }
        else if (budget > SoftMaxBudget)
        {
            violations.Add($"Budget exceeds recommended maximum (₹{SoftMaxBudget:N0}). Higher scrutiny required.");
            score -= 30;
        }

        if (budget < MinBudget)
        {
            violations.Add("Budget seems unrealistically low for a serious R&D proposal.");
            score -= 15;
        }

        // Mild penalty for very high but still under hard max
        if (budget > 20_000_000 && budget <= SoftMaxBudget)
        {
            score -= 10;
            violations.Add("Budget is on the higher side relative to typical R&D grants.");
        }

        score = Math.Clamp(score, 0, 100);
        return (Math.Round(score, 2), violations);
    }
}
