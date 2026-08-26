using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AIProposalEvaluator.Services
{
    /// <summary>
    /// Result of parsing an uploaded proposal file, regardless of source format.
    /// This is what EvaluationOrchestrator / NoveltyService / FinancialService /
    /// ReviewerChatService should consume — they should never touch PdfPig or
    /// OpenXml types directly.
    /// </summary>
    public class ParsedDocument
    {
        public string FullText { get; set; } = string.Empty;
        public List<string> PageTexts { get; set; } = new();
        public List<List<List<string>>> Tables { get; set; } = new(); // list of tables -> rows -> cells
        public string SourceFileName { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new();

        public int WordCount => FullText.Split(
            new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public class UnsupportedDocumentTypeException : Exception
    {
        public UnsupportedDocumentTypeException(string extension)
            : base($"Unsupported document type: '{extension}'. Supported: .pdf, .docx, .txt") { }
    }

    public class DocumentParserService
    {
        private readonly ILogger<DocumentParserService> _logger;
        private readonly string _uploadsDirectory;

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".txt"
        };

        public DocumentParserService(ILogger<DocumentParserService> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _uploadsDirectory = Path.Combine(env.ContentRootPath, "uploads");
            Directory.CreateDirectory(_uploadsDirectory);
        }

        public bool IsSupported(string fileName) =>
            SupportedExtensions.Contains(Path.GetExtension(fileName));

        // -----------------------------------------------------------
        // Upload handling: save the incoming file, return its saved path
        // -----------------------------------------------------------

        /// <summary>
        /// Saves an uploaded file (e.g. from IFormFile.OpenReadStream()) into
        /// uploads/ under a collision-safe generated name. Call this from
        /// /api/submit before handing the path to ParseFileAsync.
        /// </summary>
        public async Task<string> SaveUploadAsync(Stream content, string originalFileName, CancellationToken ct = default)
        {
            var ext = Path.GetExtension(originalFileName);
            if (!SupportedExtensions.Contains(ext))
                throw new UnsupportedDocumentTypeException(ext);

            var safeName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var fullPath = Path.Combine(_uploadsDirectory, safeName);

            await using var fileOut = File.Create(fullPath);
            await content.CopyToAsync(fileOut, ct);

            _logger.LogInformation("Saved upload '{Original}' as {Safe} ({Bytes} bytes)",
                originalFileName, safeName, fileOut.Length);

            return fullPath;
        }

        /// <summary>Deletes a single uploaded file once the pipeline is done with it.</summary>
        public void DeleteUpload(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Deleted upload {Path}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete upload {Path}", filePath);
            }
        }

        /// <summary>
        /// Deletes uploads older than maxAge. Call this on a timer (e.g. from a
        /// small IHostedService, or just at app startup) so failed pipeline runs
        /// don't leave orphaned files in uploads/ forever.
        /// </summary>
        public int CleanupOldUploads(TimeSpan maxAge)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            var deleted = 0;

            foreach (var file in Directory.EnumerateFiles(_uploadsDirectory))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cleanup failed for {Path}", file);
                }
            }

            if (deleted > 0)
                _logger.LogInformation("Cleaned up {Count} stale upload(s)", deleted);

            return deleted;
        }

        // -----------------------------------------------------------
        // Parsing entry points
        // -----------------------------------------------------------

        public async Task<ParsedDocument> ParseFileAsync(string filePath, CancellationToken ct = default)
        {
            await using var stream = File.OpenRead(filePath);
            return await ParseAsync(stream, Path.GetFileName(filePath), ct);
        }

        public async Task<ParsedDocument> ParseAsync(Stream fileStream, string originalFileName, CancellationToken ct = default)
        {
            var ext = Path.GetExtension(originalFileName);
            if (!SupportedExtensions.Contains(ext))
                throw new UnsupportedDocumentTypeException(ext);

            Stream workingStream = fileStream;
            if (!fileStream.CanSeek)
            {
                var buffered = new MemoryStream();
                await fileStream.CopyToAsync(buffered, ct);
                buffered.Position = 0;
                workingStream = buffered;
            }

            var result = ext.ToLowerInvariant() switch
            {
                ".pdf" => ParsePdf(workingStream, originalFileName),
                ".docx" => ParseDocx(workingStream, originalFileName),
                ".txt" => await ParseTxtAsync(workingStream, originalFileName, ct),
                _ => throw new UnsupportedDocumentTypeException(ext)
            };

            if (result.WordCount < 20)
            {
                result.Warnings.Add(
                    "Very little text was extracted. The file may be a scanned image, " +
                    "empty, or corrupted.");
            }

            return result;
        }

        // -----------------------------------------------------------
        // PDF (UglyToad.PdfPig)
        // -----------------------------------------------------------

        private ParsedDocument ParsePdf(Stream stream, string fileName)
        {
            var doc = new ParsedDocument { SourceFileName = fileName };
            var sb = new StringBuilder();

            using var pdf = PdfDocument.Open(stream);

            foreach (var page in pdf.GetPages())
            {
                string pageText;
                try
                {
                    pageText = ExtractPageTextInReadingOrder(page);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Layout-aware extraction failed for page {Page}, using fallback", page.Number);
                    pageText = page.Text;
                    doc.Warnings.Add($"Page {page.Number}: used simple text extraction (layout analysis failed).");
                }

                if (string.IsNullOrWhiteSpace(pageText) && page.NumberOfImages > 0)
                {
                    doc.Warnings.Add(
                        $"Page {page.Number} has no extractable text but contains {page.NumberOfImages} image(s) " +
                        "— it may be a scanned page (not OCR'd).");
                }

                doc.PageTexts.Add(pageText);
                sb.AppendLine(pageText);
                sb.AppendLine();

                foreach (var table in DetectSimpleTables(page))
                    doc.Tables.Add(table);
            }

            doc.FullText = sb.ToString().Trim();
            return doc;
        }

        /// <summary>
        /// Extracts text respecting reading order, including multi-column layouts.
        /// PdfPig's default page.Text follows raw content-stream order, which
        /// interleaves lines across columns on two-column proposal PDFs. Instead:
        ///   1. NearestNeighbourWordExtractor for cleaner word grouping
        ///   2. DocstrumBoundingBoxes to segment into spatially-coherent blocks
        ///      (this is what correctly separates left/right columns)
        ///   3. Walk blocks/lines in the segmenter's determined order
        /// </summary>
        private static string ExtractPageTextInReadingOrder(Page page)
        {
            var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList();
            if (words.Count == 0) return string.Empty;

            var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);

            var sb = new StringBuilder();
            foreach (var block in blocks)
            {
                foreach (var line in block.TextLines)
                    sb.AppendLine(line.Text);
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Conservative heuristic table detector: groups words into rows by Y
        /// position, then flags runs of 3+ consecutive rows with a consistent
        /// cell count as a likely table (e.g. budget line items for FinancialService).
        /// </summary>
        private static List<List<List<string>>> DetectSimpleTables(Page page)
        {
            var tables = new List<List<List<string>>>();
            var words = page.GetWords().ToList();
            if (words.Count == 0) return tables;

            var rows = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0) * 3.0)
                .OrderByDescending(g => g.Key)
                .Select(g => g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text).ToList())
                .Where(r => r.Count >= 3)
                .ToList();

            if (rows.Count < 3) return tables;

            var candidate = new List<List<string>>();
            foreach (var row in rows)
            {
                if (candidate.Count == 0 || Math.Abs(row.Count - candidate[^1].Count) <= 1)
                {
                    candidate.Add(row);
                }
                else
                {
                    if (candidate.Count >= 3) tables.Add(candidate);
                    candidate = new List<List<string>> { row };
                }
            }
            if (candidate.Count >= 3) tables.Add(candidate);

            return tables;
        }

        // -----------------------------------------------------------
        // DOCX (DocumentFormat.OpenXml)
        // -----------------------------------------------------------

        private ParsedDocument ParseDocx(Stream stream, string fileName)
        {
            var doc = new ParsedDocument { SourceFileName = fileName };
            var sb = new StringBuilder();

            using var wordDoc = WordprocessingDocument.Open(stream, false);
            var body = wordDoc.MainDocumentPart?.Document.Body;

            if (body is null)
            {
                doc.Warnings.Add("The .docx file has no readable body content.");
                return doc;
            }

            foreach (var element in body.Elements())
            {
                switch (element)
                {
                    case Paragraph para:
                        if (!string.IsNullOrWhiteSpace(para.InnerText))
                            sb.AppendLine(para.InnerText);
                        break;

                    case Table table:
                        var rows = table.Elements<TableRow>()
                            .Select(r => r.Elements<TableCell>().Select(c => c.InnerText.Trim()).ToList())
                            .ToList();
                        doc.Tables.Add(rows);
                        foreach (var row in rows)
                            sb.AppendLine(string.Join(" | ", row));
                        sb.AppendLine();
                        break;
                }
            }

            doc.FullText = sb.ToString().Trim();
            doc.PageTexts.Add(doc.FullText); // .docx has no native page concept without rendering
            return doc;
        }

        // -----------------------------------------------------------
        // TXT
        // -----------------------------------------------------------

        private async Task<ParsedDocument> ParseTxtAsync(Stream stream, string fileName, CancellationToken ct)
        {
            var doc = new ParsedDocument { SourceFileName = fileName };
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(ct);

            doc.FullText = text.Trim();
            doc.PageTexts.Add(doc.FullText);
            return doc;
        }
    }
}
