using Microsoft.Data.Sqlite;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace rag_example.Service;

public class PolicyService
{
    private readonly ChatClient _chatClient;
    private readonly EmbeddingClient _embeddingClient;
    private readonly string _policyPath;
    private readonly string _dbPath;

    public PolicyService(IConfiguration config)
    {
        var apiKey = config["OpenAi:ApiKey"];
        
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Missing required Api Key!");
        }
        
        var client = new OpenAIClient(apiKey);

        _chatClient = client.GetChatClient("gpt-4o-mini");
        _embeddingClient = client.GetEmbeddingClient("text-embedding-3-small");

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
        var chunks = SplitIntoChunks(policyText, 500);
        
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
                var embeddingResult = await _embeddingClient.GenerateEmbeddingAsync(chunk);

                float[] vector = embeddingResult.Value.ToFloats().ToArray();

                var insertComand = conn.CreateCommand();
                
                insertComand.CommandText = "INSERT INTO PolicyChunks (Text, Embedding) VALUES ($text, $embedding)";

                insertComand.Parameters.AddWithValue("$text", chunk);
                insertComand.Parameters.AddWithValue("$embedding", FloatArrayToBytes(vector));

                insertComand.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }

    public async Task<string> GetAnswerAsync(string question)
    {
        var queryEmbedding = await _embeddingClient.GenerateEmbeddingAsync(question);
        var queryVector = queryEmbedding.Value.ToFloats().ToArray();

        var topChunks = GetTopChunks(queryVector, 3);

        var context = string.Join("\n\n", topChunks);

        List<ChatMessage> messages = new()
        {
            ChatMessage.CreateSystemMessage(
                "You are a helpful assistant that answers based on company return policies."),
            ChatMessage.CreateUserMessage(
                $"Use only the following policy text to answer the question:\n\n{context}\n\nQuestion: {question}")
        };

        var response = await _chatClient.CompleteChatAsync(messages);
        return response.Value.Content[0].Text.Trim();
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