using Microsoft.Data.Sqlite;
using OllamaSharp;
using OllamaSharp.Models;

namespace rag_example.Service;

public class PolicyService
{
    private readonly OllamaApiClient _ollama;
    private readonly string _policyPath;
    private readonly string _dbPath;

    private const string ChatModel = "llama3.2";
    private const string EmbedModel = "nomic-embed-text";

    public PolicyService(IConfiguration config)
    {
        _ollama = new OllamaApiClient("http://localhost:11434");

        _policyPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "return_policy.txt");
        _dbPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "embeddings.db");

        InitializeDatabase();
        LoadPolicyIntoDatabaseAsync().GetAwaiter().GetResult();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS PolicyChunks (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Text TEXT NOT NULL,
                        Embedding BLOB NOT NULL
                        );";
        cmd.ExecuteNonQuery();
    }

    private async Task LoadPolicyIntoDatabaseAsync()
    {
        var policyText = await File.ReadAllTextAsync(_policyPath);
        var chunks = SplitIntoChunks(policyText, 500).ToList();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        foreach (var chunk in chunks)
        {
            var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM PolicyChunks WHERE Text = $text";
            checkCmd.Parameters.AddWithValue("$text", chunk);
            bool exists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
            if (exists) continue;

            try
            {
                var embedResponse = await _ollama.EmbedAsync(new EmbedRequest
                {
                    Model = EmbedModel,
                    Input = [chunk]
                });

                float[] vector = embedResponse.Embeddings[0];

                var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = "INSERT INTO PolicyChunks (Text, Embedding) VALUES ($text, $embedding)";
                insertCmd.Parameters.AddWithValue("$text", chunk);
                insertCmd.Parameters.AddWithValue("$embedding", FloatArrayToBytes(vector));
                insertCmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }

    public async Task<string> GetAnswerAsync(string question)
    {
        var embedResponse = await _ollama.EmbedAsync(new EmbedRequest
        {
            Model = EmbedModel,
            Input = [question]
        });

        var queryVector = embedResponse.Embeddings[0];
        var topChunks = GetTopChunks(queryVector, 3);
        var context = string.Join("\n\n", topChunks);

        var prompt = $"Use only the following policy text to answer the question:\n\n{context}\n\nQuestion: {question}";

        var fullResponse = "";

        await foreach (var chunk in _ollama.GenerateAsync(new GenerateRequest
        {
            Model = ChatModel,
            Prompt = prompt,
            System = "You are a helpful assistant that answers based on company return policies.",
            Stream = true
        }))
        {
            fullResponse += chunk?.Response ?? "";
        }

        return fullResponse.Trim();
    }

    private List<string> GetTopChunks(float[] queryEmbedding, int topN)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT Text, Embedding FROM PolicyChunks";

        using var reader = selectCmd.ExecuteReader();
        var scoredChunks = new List<(string Text, double Score)>();

        while (reader.Read())
        {
            var text = reader.GetString(0);
            var embeddingBytes = (byte[])reader["Embedding"];
            var embedding = BytesToFloatArray(embeddingBytes);
            var similarity = CosineSimilarity(embedding, queryEmbedding);
            scoredChunks.Add((text, similarity));
        }

        return scoredChunks
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .Select(x => x.Text)
            .ToList();
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int maxLength)
    {
        for (int i = 0; i < text.Length; i += maxLength)
            yield return text.Substring(i, Math.Min(maxLength, text.Length - i));
    }

    private static double CosineSimilarity(float[] v1, float[] v2)
    {
        double dot = 0.0, mag1 = 0.0, mag2 = 0.0;
        for (int i = 0; i < v1.Length; i++)
        {
            dot += v1[i] * v2[i];
            mag1 += v1[i] * v1[i];
            mag2 += v2[i] * v2[i];
        }
        return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2));
    }

    private static byte[] FloatArrayToBytes(float[] array)
    {
        var bytes = new byte[array.Length * sizeof(float)];
        Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloatArray(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}