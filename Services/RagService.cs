using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class RagService
{
    private readonly ILogger<RagService> _logger;
    private readonly List<TextChunk> _chunks = new();
    private readonly Dictionary<string, Dictionary<int, int>> _termFrequency = new(); // term -> (chunkIndex -> count)
    private readonly Dictionary<int, int> _chunkLengths = new(); // chunkIndex -> word count
    private double _avgChunkLength;
    private bool _isIndexing;

    public RagService(ILogger<RagService> logger)
    {
        _logger = logger;
    }

    public bool IsIndexing => _isIndexing;
    public int ChunkCount => _chunks.Count;

    public void StartIndexing(List<string> directories)
    {
        if (_isIndexing) return;
        _isIndexing = true;

        Task.Run(() =>
        {
            try
            {
                _chunks.Clear();
                _termFrequency.Clear();
                _chunkLengths.Clear();

                var allowedExtensions = new HashSet<string> { ".txt", ".cs", ".md", ".json", ".xml", ".yaml", ".yml", ".py", ".js", ".ts", ".html", ".css", ".java", ".cpp", ".h", ".sql", ".log", ".cfg", ".ini" };

                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir)) continue;

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                        {
                            var ext = Path.GetExtension(file).ToLowerInvariant();
                            if (!allowedExtensions.Contains(ext)) continue;

                            try
                            {
                                var info = new FileInfo(file);
                                if (info.Length > 1024 * 1024) continue; // skip files > 1MB

                                var lines = File.ReadAllLines(file);
                                ChunkFile(file, lines);
                            }
                            catch (Exception ex) { _logger.LogDebug(ex, "Skipping RAG file {File}", file); }
                        }
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to enumerate RAG directory {Dir}", dir); }
                }

                // Build term frequency index
                for (int i = 0; i < _chunks.Count; i++)
                {
                    var words = Tokenize(_chunks[i].Content);
                    _chunkLengths[i] = words.Length;

                    foreach (var word in words)
                    {
                        if (!_termFrequency.ContainsKey(word))
                            _termFrequency[word] = new Dictionary<int, int>();

                        if (!_termFrequency[word].ContainsKey(i))
                            _termFrequency[word][i] = 0;
                        _termFrequency[word][i]++;
                    }
                }

                _avgChunkLength = _chunkLengths.Count > 0 ? _chunkLengths.Values.Average() : 1;
                _logger.LogInformation("RAG indexing complete: {Count} chunks from {Dirs} directories", _chunks.Count, directories.Count);
            }
            finally
            {
                _isIndexing = false;
            }
        });
    }

    private void ChunkFile(string filePath, string[] lines, int chunkSize = 500, int overlap = 50)
    {
        var currentChunk = new List<string>();
        int currentWordCount = 0;
        int startLine = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            currentChunk.Add(lines[i]);
            currentWordCount += lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            if (currentWordCount >= chunkSize)
            {
                _chunks.Add(new TextChunk
                {
                    FilePath = filePath,
                    Content = string.Join("\n", currentChunk),
                    StartLine = startLine + 1
                });

                // Keep overlap lines
                int overlapWords = 0;
                int overlapStart = currentChunk.Count - 1;
                while (overlapStart > 0 && overlapWords < overlap)
                {
                    overlapWords += currentChunk[overlapStart].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    overlapStart--;
                }

                var overlapLines = currentChunk.Skip(overlapStart + 1).ToList();
                currentChunk = overlapLines;
                currentWordCount = overlapWords;
                startLine = i - overlapLines.Count + 1;
            }
        }

        if (currentChunk.Count > 0)
        {
            _chunks.Add(new TextChunk
            {
                FilePath = filePath,
                Content = string.Join("\n", currentChunk),
                StartLine = startLine + 1
            });
        }
    }

    private static string[] Tokenize(string text)
    {
        return Regex.Split(text.ToLowerInvariant(), @"\W+")
            .Where(w => w.Length >= 2)
            .ToArray();
    }

    public List<TextChunk> RetrieveRelevant(string query, int topK = 3)
    {
        if (_chunks.Count == 0) return new List<TextChunk>();

        var queryTerms = Tokenize(query);
        var scores = new Dictionary<int, double>();

        double k1 = 1.5, b = 0.75;
        int N = _chunks.Count;

        foreach (var term in queryTerms)
        {
            if (!_termFrequency.ContainsKey(term)) continue;

            var postings = _termFrequency[term];
            double idf = Math.Log((N - postings.Count + 0.5) / (postings.Count + 0.5) + 1);

            foreach (var (chunkIdx, tf) in postings)
            {
                double dl = _chunkLengths.GetValueOrDefault(chunkIdx, 1);
                double score = idf * (tf * (k1 + 1)) / (tf + k1 * (1 - b + b * dl / _avgChunkLength));

                if (!scores.ContainsKey(chunkIdx))
                    scores[chunkIdx] = 0;
                scores[chunkIdx] += score;
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv =>
            {
                var chunk = _chunks[kv.Key];
                return new TextChunk
                {
                    FilePath = chunk.FilePath,
                    Content = chunk.Content,
                    StartLine = chunk.StartLine,
                    Score = kv.Value
                };
            })
            .ToList();
    }

    public string BuildAugmentedPrompt(string query, List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return query;

        var contextParts = chunks.Select(c =>
            $"--- From {Path.GetFileName(c.FilePath)} (line {c.StartLine}) ---\n{(c.Content.Length > 1500 ? c.Content[..1500] + "..." : c.Content)}");

        return $"Context from local files:\n{string.Join("\n\n", contextParts)}\n\nUser question: {query}";
    }
}
