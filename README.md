# AI Proposal Evaluator (.NET Edition)

A full-featured **.NET 8** port of the original [AI-Proposal-Evaluator](https://github.com/kavya1b1/AI-Proposal-Evaluator) Python project.

This system automatically evaluates R&D / research proposals by:

- Parsing proposal PDFs
- Scoring **novelty** against a historical project corpus (TF-IDF cosine similarity)
- Checking **financial feasibility** against budget guidelines
- Running an **ensemble-style ML evaluation** with uncertainty quantification
- Providing **explainable AI** insights (feature importance + SHAP-style contributions)
- Generating a professional **PDF report** (QuestPDF)
- Offering an optional **GenAI narrative** and **Reviewer Agent chatbot** (via OpenRouter / any OpenAI-compatible API)

## Architecture

```
Browser (Blazor Server UI)
        │
        ▼
ASP.NET Core 8 Host
├── Minimal APIs  (/api/submit, /api/ask, /api/history)
├── Blazor Server pages (Home / Chat / History)
└── Services
    ├── DocumentParserService   (UglyToad.PdfPig)
    ├── NoveltyService          (TF-IDF + cosine similarity)
    ├── FinancialService
    ├── MlEvaluationService     (weighted ensemble + uncertainty)
    ├── NarrativeService        (OpenRouter or rule-based fallback)
    ├── ReviewerChatService
    ├── ReportService           (QuestPDF)
    └── EvaluationOrchestrator
        │
        ▼
SQLite (EF Core) + reports/ + uploads/
```

## Features parity with original

| Feature                        | Original (Python)      | .NET Edition                  |
|--------------------------------|------------------------|-------------------------------|
| PDF text extraction            | pdfplumber             | UglyToad.PdfPig               |
| Novelty / similarity           | TF-IDF (sklearn)       | Pure C# TF-IDF + cosine       |
| Financial checks               | Rule-based             | Rule-based (same limits)      |
| ML ensemble + uncertainty      | sklearn RandomForest   | Weighted ensemble + noise     |
| SHAP explanations              | shap library           | SHAP-style contribution approx|
| GenAI narrative                | OpenRouter             | OpenRouter (optional)         |
| Reviewer chatbot               | OpenRouter             | OpenRouter (optional)         |
| PDF report                     | ReportLab              | QuestPDF                      |
| Frontend                       | Streamlit              | Blazor Server (Bootstrap 5)   |
| Persistence                    | SQLAlchemy + SQLite    | EF Core + SQLite              |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- (Optional) OpenRouter API key for real GenAI narratives & chat

## Quick Start

```bash
# Clone / copy the project
cd AI-Proposal-Evaluator-DotNet

# Restore & run
cd src/AIProposalEvaluator
dotnet restore
dotnet run
```

Open the browser at the URL shown (usually `https://localhost:5xxx` or `http://localhost:5xxx`).

### Optional: enable GenAI

Set the environment variable or put the key in `appsettings.json`:

```bash
export OPENROUTER_API_KEY=sk-or-v1-xxxxxxxx
dotnet run
```

or in `appsettings.json`:

```json
"OpenRouter": {
  "ApiKey": "sk-or-v1-xxxxxxxx"
}
```

Without a key the system still works fully — it falls back to high-quality rule-based narratives and answers.

## API Endpoints

| Method | Path            | Description                          |
|--------|-----------------|--------------------------------------|
| POST   | `/api/submit`   | Upload PDF + budget → full evaluation|
| POST   | `/api/ask`      | Ask the Reviewer Agent               |
| GET    | `/api/history`  | Last N evaluation records            |

Example `curl`:

```bash
curl -X POST http://localhost:5000/api/submit \
  -F "file=@my_proposal.pdf" \
  -F "budget=2500000"
```

## Project Structure

```
AI-Proposal-Evaluator-DotNet/
├── AIProposalEvaluator.sln
├── README.md
├── data/
│   └── past_projects.csv
├── src/
│   └── AIProposalEvaluator/
│       ├── AIProposalEvaluator.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Models/
│       ├── Data/
│       ├── Services/          ← all domain logic
│       ├── Pages/             ← Blazor UI
│       ├── Shared/
│       └── wwwroot/
├── uploads/                   (created at runtime)
└── reports/                   (created at runtime)
```

## Decision Rules (same as original)

| Final Score | Decision                              |
|-------------|---------------------------------------|
| ≥ 85        | Strongly Recommended for Funding      |
| ≥ 70        | Recommended with Minor Revisions      |
| < 70        | Not Recommended                       |

## Notes on the ML model

The original project shipped a tiny RandomForest trained on 5 synthetic samples.  
This .NET version re-implements the same spirit with a transparent weighted ensemble + controlled variance so that:

- Uncertainty / confidence bands are meaningful
- No external `.pkl` dependency is required
- Behaviour is deterministic given the same inputs (seeded RNG)

You can later replace `MlEvaluationService` with a real ML.NET FastTree / LightGBM model if you have a larger labelled dataset.

## License

This is a clean-room re-implementation for educational / demonstration purposes.
Original concept & design credit: Kavya Gupta (AI-Proposal-Evaluator).
