using ANNIndexingSample.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;

namespace ANNIndexingSample
{
    /// <summary>
    /// Provides central management for database connectivity, status monitoring, and data seeding.
    /// This class handles the retrieval of credentials from local configuration and validates the health of the ANN index.
    /// </summary>
    internal class DBHandler
    {
        /// <summary>
        /// Retrieves and builds a SQL Server connection string by reading environment variables 
        /// from the local 'launchSettings.json' file.
        /// </summary>
        /// <returns>A fully formatted SQL connection string.</returns>
        public static string GetConnectionString()
        {
            // 1. Setup the configuration builder to look into the Properties folder
            IConfiguration config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("Properties/launchSettings.json", optional: false, reloadOnChange: true)
                .Build();

            // 2. Define the path to the environment variables section
            string envPath = "profiles:ANNIndexingSample:environmentVariables";

            // 3. Extract the values using the hierarchical keys
            string server = config[$"{envPath}:DB_SERVER"] ?? "";
            string database = config[$"{envPath}:DB_NAME"] ?? "";
            string user = config[$"{envPath}:DB_USER"] ?? "";
            string password = config[$"{envPath}:DB_PASSWORD"] ?? "";

            // 4. Build the connection string safely
            SqlConnectionStringBuilder builder = new()
            {
                DataSource = server,
                InitialCatalog = database,
                UserID = user,
                Password = password,
                Encrypt = true,
                TrustServerCertificate = false,
                ConnectTimeout = 30
            };

            return builder.ConnectionString;
        }

        /// <summary>
        /// Validates the database connection and generates a status report. 
        /// It checks if the Movie and Centroid tables are populated and warns if the ANN index needs synchronization.
        /// </summary>
        /// <param name="connectionString">The connection string used to reach the database.</param>
        /// <returns>True if the connection is successful; otherwise, false.</returns>
        public static async Task<bool> CheckDatabaseStatus(string connectionString)
        {
            // 1. Initialize Configuration inside the function to retrieve table names
            IConfiguration config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("Properties/launchSettings.json", optional: false, reloadOnChange: true)
                .Build();

            // 2. Extract table names from the specific JSON path
            string envPath = "profiles:ANNIndexingSample:environmentVariables";
            string movieTable = config[$"{envPath}:TABLE_MOVIES"] ?? "Movies";
            string centroidTable = config[$"{envPath}:TABLE_CENTROIDS"] ?? "MovieCentroids";

            Console.WriteLine("\n--- 🔍 Checking Database Status ---");

            var builder = new SqlConnectionStringBuilder(connectionString);

            using SqlConnection conn = new(connectionString);
            try
            {
                await conn.OpenAsync();

                // 3. Perform counts using the dynamic table names
                int movieCount = await GetCount(conn, movieTable);
                int centroidCount = await GetCount(conn, centroidTable);
                int indexedCount = await GetCount(conn, $"{movieTable} WHERE cluster_id IS NOT NULL");

                Console.WriteLine("✅ Connection: SUCCESSFUL");
                Console.WriteLine($"📍 Server:     {builder.DataSource}");
                Console.WriteLine($"📂 Database:   {builder.InitialCatalog}");
                Console.WriteLine("------------------------------------------");
                Console.WriteLine($"🎬 Total [{movieTable}]:    {movieCount:N0}");
                Console.WriteLine($"📍 Total [{centroidTable}]: {centroidCount:N0}");
                Console.WriteLine($"🔗 Indexed Items:           {indexedCount:N0}");

                // 4. Logic check: If movies exist but aren't assigned to clusters, the ANN index is out of date.
                if (indexedCount < movieCount && movieCount > 0)
                {
                    Console.WriteLine("\n⚠️  Warning: Index is out of date. Please run the K-Means sync.");
                }

                Console.WriteLine("------------------------------------------\n");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Connection: OFFLINE");
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Private helper to execute a SQL COUNT query. 
        /// Gracefully handles cases where the table may not yet exist in the schema.
        /// </summary>
        /// <param name="conn">An open SqlConnection object.</param>
        /// <param name="tableWithCriteria">The table name, potentially including a WHERE clause.</param>
        /// <returns>The number of rows found, or 0 if the query fails.</returns>
        private static async Task<int> GetCount(SqlConnection conn, string tableWithCriteria)
        {
            try
            {
                string sql = $"SELECT COUNT(*) FROM {tableWithCriteria}";
                using SqlCommand cmd = new(sql, conn);
                return (int)(await cmd.ExecuteScalarAsync() ?? 0);
            }
            catch
            {
                // Return 0 if the table doesn't exist yet (common during first run)
                return 0;
            }
        }

        /// <summary>
        /// Orchestrates the initial population of the database by reading from a local source 
        /// and generating vector embeddings via the OpenAI client.
        /// </summary>
        /// <param name="connString">The database connection string.</param>
        /// <param name="client">The OpenAI EmbeddingClient used to generate vector data.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task SeedDatabaseAsync(string connString, OpenAI.Embeddings.EmbeddingClient client)
        {
            Console.WriteLine("Starting Seeding Process...");
            // Delegates the actual CSV parsing and SQL insertion to the DataSeeder service
            await DataSeeder.SeedMoviesFromCsvAsync(connString, client);
            Console.WriteLine("Seeding Complete.");
        }
    }
}