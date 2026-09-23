using ANNIndexingSample.Database;
using ANNIndexingSample.Helper;
using ANNIndexingSample.Services;
using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;

namespace ANNIndexingSample
{
    /// <summary>
    /// The main entry point for the Movie Vector System. 
    /// Provides a console-based interface for database seeding, ANN index management, 
    /// and performance benchmarking between ANN and Exact search modes.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Orchestrates the application lifecycle, handling the main menu loop 
        /// and delegating tasks to specific handlers based on user input.
        /// </summary>
        /// <param name="args">Command-line arguments (not used).</param>
        static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 App Starting...");
            // Initialize the configuration to read from the Properties folder
            string connString = DBHandler.GetConnectionString();
            IConfiguration config = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddJsonFile("Properties/launchSettings.json", optional: false, reloadOnChange: true)
                            .Build();

            string apiKey = config["profiles:ANNIndexingSample:environmentVariables:OPENAI_API_KEY"] ?? "";

            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("❌ CRITICAL ERROR: OpenAI API Key not found in launchSettings.json.");
                return;
            }
            // Initialize the embedding client with the selected model
            EmbeddingClient client = new("text-embedding-3-small", apiKey);

            // Enter the Main Application Loop
            // The loop continues as long as the database status check passes
            while (await DBHandler.CheckDatabaseStatus(connString).ConfigureAwait(true))
            {
                Console.WriteLine("\n--- MOVIE VECTOR SYSTEM ---");
                Console.WriteLine("1. Seeding Movies Data (CSV -> SQL)");
                Console.WriteLine("2. Generating Centroids (User-defined Clusters)");
                Console.WriteLine("3. Assigning Clusters (Rebuild ANN Index)");
                Console.WriteLine("4. Searching Movies (ANN Mode)");
                Console.WriteLine("5. Searching Movies (Exact Mode - Brute Force)");
                Console.WriteLine("6. Restarting Database Connection");
                Console.WriteLine("7. Exit");
                Console.Write("\nSelect Option: ");

                string choice = Console.ReadLine() ?? "";

                switch (choice)
                {
                    case "1":
                        await DBHandler.SeedDatabaseAsync(connString, client).ConfigureAwait(true);
                        break;
                    case "2":
                        Console.Write("How many clusters (centroids) do you want to create? (e.g., 100): ");
                        if (int.TryParse(Console.ReadLine(), out int k) && k > 0)
                        {
                            await KMeansManager.GenerateCentroidsAsync(connString, k).ConfigureAwait(true);
                        }
                        break;
                    case "3":
                        await KMeansManager.AssignMoviesToClustersAsync(connString).ConfigureAwait(true);
                        break;
                    case "4":
                        await RunANNSearch(connString, client).ConfigureAwait(true);
                        break;
                    case "5":
                        await RunExactSearch(connString, client).ConfigureAwait(true);
                        break;
                    case "6":
                        await DBHandler.CheckDatabaseStatus(connString).ConfigureAwait(true);
                        break;
                    case "7":
                        return;
                    default:
                        Console.WriteLine("Invalid option. Try again.");
                        break;
                }
            }
        }

        /// <summary>
        /// Executes a single Approximate Nearest Neighbor (ANN) search trial.
        /// Converts the user's text query into a vector and searches within the most relevant cluster.
        /// </summary>
        /// <param name="conn">The SQL connection string.</param>
        /// <param name="ec">The OpenAI EmbeddingClient.</param>
        static async Task RunANNSearch(string conn, EmbeddingClient ec)
        {
            Console.Write("\nEnter search query (or 'back'): ");
            string query = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(query) || query.ToLower() == "back") return;
            Console.WriteLine("✨ Generating embedding for the query...");
            try
            {
                var embeddingResult = await ec.GenerateEmbeddingsAsync([query]).ConfigureAwait(true);
                float[] queryVector = embeddingResult.Value[0].ToFloats().ToArray();

                await KMeansManager.SearchANNAsync(conn, queryVector).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Search Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a single Exact (Brute-Force) search trial.
        /// Compares the query vector against every record in the database for maximum accuracy (Ground Truth).
        /// </summary>
        /// <param name="conn">The SQL connection string.</param>
        /// <param name="ec">The OpenAI EmbeddingClient.</param>
        static async Task RunExactSearch(string conn, EmbeddingClient ec)
        {
            Console.Write("\nEnter search query for EXACT search (or 'back'): ");
            string query = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(query) || query.ToLower() == "back") return;
            Console.WriteLine("✨ Generating embedding for the query...");

            try
            {
                Console.WriteLine("✨ Generating embedding...");
                var embeddingResult = await ec.GenerateEmbeddingsAsync([query]).ConfigureAwait(true);
                float[] queryVector = embeddingResult.Value[0].ToFloats().ToArray();

                Console.WriteLine("🐢 Running Brute-Force Exact Search (Scanning all rows)...");
                float time = await KMeansManager.SearchExactAsync(conn, queryVector).ConfigureAwait(true);

                Console.WriteLine($"\n🐢 Exact Search Time: {time:F2} ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Search Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs multiple trials of Exact Search for benchmarking purposes.
        /// Results are logged to a local file for performance analysis.
        /// </summary>
        /// <param name="conn">The SQL connection string.</param>
        /// <param name="ec">The OpenAI EmbeddingClient.</param>
        static async Task RunExactSearchLoop(string conn, EmbeddingClient ec)
        {
            Console.Write("\n[EXACT MODE] Quantity of trials for benchmarking: ");
            if (!int.TryParse(Console.ReadLine(), out int searchCnt)) searchCnt = 1;

            Console.Write("Enter search query (or 'back'): ");
            string query = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(query) || query.ToLower() == "back") return;

            try
            {
                Console.WriteLine("✨ Generating query vector...");
                var embeddingResult = await ec.GenerateEmbeddingsAsync([query]).ConfigureAwait(true);
                float[] queryVector = embeddingResult.Value[0].ToFloats().ToArray();

                List<float> recordedTimes = new();
                Console.WriteLine($"🐢 Starting {searchCnt} Exact Search trials...");

                for (int i = 0; i < searchCnt; i++)
                {
                    float time = await KMeansManager.SearchExactAsync(conn, queryVector).ConfigureAwait(true);
                    recordedTimes.Add(time);
                    Console.WriteLine($"Trial {i + 1}: {time:F2} ms");
                }

                await ResultLogger.SaveSearchRecordAsync($"{query} [EXACT]", recordedTimes).ConfigureAwait(true);
                Console.WriteLine($"\n✅ Done! Exact search data saved to records.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exact Search Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs multiple trials of ANN Search for benchmarking purposes.
        /// Results are logged to a local file to compare against Exact Search performance.
        /// </summary>
        /// <param name="conn">The SQL connection string.</param>
        /// <param name="ec">The OpenAI EmbeddingClient.</param>
        static async Task RunSearchLoop(string conn, EmbeddingClient ec)
        {
            Console.Write("Quantity of trials for this search: ");
            if (!int.TryParse(Console.ReadLine(), out int searchCnt)) searchCnt = 1;

            Console.Write("\nEnter search query (or 'back'): ");
            string query = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(query) || query.ToLower() == "back") return;

            try
            {
                var embeddingResult = await ec.GenerateEmbeddingsAsync([query]).ConfigureAwait(true);
                float[] queryVector = embeddingResult.Value[0].ToFloats().ToArray();

                List<float> recordedTimes = new();
                for (int i = 0; i < searchCnt; i++)
                {
                    float time = await KMeansManager.SearchANNAsync(conn, queryVector).ConfigureAwait(true);
                    recordedTimes.Add(time);
                    Console.WriteLine($"Trial {i + 1}: {time:F2} ms");
                }

                await ResultLogger.SaveSearchRecordAsync(query, recordedTimes).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Search Error: {ex.Message}");
            }
        }
    }
}