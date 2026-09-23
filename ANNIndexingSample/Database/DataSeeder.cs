using System.Globalization;
using ANNIndexingSample.Entities;
using Microsoft.Data.SqlClient;
using OpenAI.Embeddings;

namespace ANNIndexingSample.Database
{
    /// <summary>
    /// Handles the initial data population by reading movie records from a CSV file, 
    /// generating high-dimensional vector embeddings via OpenAI, and persisting the results to SQL Server.
    /// </summary>
    internal class DataSeeder
    {
        /// <summary>
        /// The relative path to the source CSV file containing movie metadata.
        /// </summary>
        private const string CsvPath = "Data/data.csv";

        /// <summary>
        /// Orchestrates the seeding process. Reads the CSV, partitions records into batches, 
        /// and triggers the embedding and insertion logic.
        /// </summary>
        /// <param name="connectionString">The SQL Server connection string.</param>
        /// <param name="client">The OpenAI EmbeddingClient used to transform text into vectors.</param>
        /// <returns>A task representing the asynchronous seeding operation.</returns>
        public static async Task SeedMoviesFromCsvAsync(string connectionString, EmbeddingClient client)
        {
            Console.WriteLine($"--- Seeding Data from {CsvPath} ---");

            // Safety check to ensure the file was correctly copied to the output directory
            if (!File.Exists(CsvPath))
            {
                Console.WriteLine($"Error: CSV file not found at {Path.GetFullPath(CsvPath)}");
                return;
            }

            // Load all data, skipping the CSV header row
            var lines = (await File.ReadAllLinesAsync(CsvPath)).Skip(1).ToList();

            // Process in batches (default: 10) to optimize network latency and API throughput
            int batchSize = 10;
            for (int i = 0; i < lines.Count; i += batchSize)
            {
                var batchLines = lines.Skip(i).Take(batchSize).ToList();
                await ProcessBatch(connectionString, batchLines, client);
            }

            Console.WriteLine("--- Seeding Complete ---");
        }

        /// <summary>
        /// Processes a specific batch of CSV lines. 
        /// This involves parsing text, generating embeddings for the batch, and executing SQL INSERT commands.
        /// </summary>
        /// <param name="connString">The SQL Server connection string.</param>
        /// <param name="lines">The subset of CSV lines to process.</param>
        /// <param name="client">The OpenAI EmbeddingClient.</param>
        /// <returns>A task representing the asynchronous batch operation.</returns>
        private static async Task ProcessBatch(string connString, List<string> lines, EmbeddingClient client)
        {
            List<string> inputsToEmbed = new();
            List<MovieData> movieMetadata = new();

            // 1. Parse CSV lines into metadata objects and string inputs for the embedding model
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 5) continue;

                var movie = new MovieData
                {
                    Id = int.Parse(parts[0]),
                    Title = parts[1],
                    Genre = parts[2],
                    Year = int.Parse(parts[3]),
                    Description = parts[4]
                };

                movieMetadata.Add(movie);
                // Combine Title and Description to provide more semantic context for the vector
                inputsToEmbed.Add($"{movie.Title}: {movie.Description}");
            }

            try
            {
                // 2. Bulk generate embeddings for the entire batch in a single API call
                OpenAIEmbeddingCollection embeddings = await client.GenerateEmbeddingsAsync(inputsToEmbed).ConfigureAwait(true);

                using SqlConnection conn = new(connString);
                await conn.OpenAsync().ConfigureAwait(true);

                // 3. Map embeddings back to metadata and insert into the database
                for (int j = 0; j < movieMetadata.Count; j++)
                {
                    float[] vector = embeddings[j].ToFloats().ToArray();

                    // Format the float array as a JSON string compatible with SQL Server's VECTOR type
                    string vectorJson = $"[{string.Join(",", vector.Select(f => f.ToString(CultureInfo.InvariantCulture)))}]";

                    string sql = @"INSERT INTO Movies (id, title, genre, release_year, description, embedding) 
                                   VALUES (@Id, @Title, @Genre, @Year, @Desc, @Vector)";

                    using SqlCommand cmd = new(sql, conn);
                    cmd.Parameters.AddWithValue("@Id", movieMetadata[j].Id);
                    cmd.Parameters.AddWithValue("@Title", movieMetadata[j].Title);
                    cmd.Parameters.AddWithValue("@Genre", movieMetadata[j].Genre);
                    cmd.Parameters.AddWithValue("@Year", movieMetadata[j].Year);
                    cmd.Parameters.AddWithValue("@Desc", movieMetadata[j].Description);
                    cmd.Parameters.AddWithValue("@Vector", vectorJson);

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(true);
                    Console.WriteLine($"[Seeded ID {movieMetadata[j].Id}]: {movieMetadata[j].Title}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Batch Error]: {ex.Message}");
            }
        }
    }
}