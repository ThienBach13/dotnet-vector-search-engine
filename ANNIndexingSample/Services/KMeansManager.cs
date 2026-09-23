using System.Globalization;
using Microsoft.Data.SqlClient;
using System.Diagnostics;

namespace ANNIndexingSample.Services
{
    /// <summary>
    /// Manages the lifecycle of K-Means clustering for Approximate Nearest Neighbor (ANN) indexing.
    /// Includes methods for centroid generation, movie partitioning, and performance-based similarity searching.
    /// </summary>
    internal class KMeansManager
    {
        /// <summary>
        /// Partitions existing movies into semantic clusters by assigning each movie 
        /// to its nearest centroid based on Euclidean distance.
        /// </summary>
        /// <param name="connString">The SQL Server connection string.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task AssignMoviesToClustersAsync(string connString)
        {
            Console.WriteLine("Partitioning movies into semantic clusters...");

            using SqlConnection conn = new(connString);
            await conn.OpenAsync();

            // This query uses CROSS APPLY to find the single closest centroid for every movie
            string sql = @"
                            UPDATE m
                            SET m.cluster_id = c.cluster_id
                            FROM Movies m
                            CROSS APPLY (
                                SELECT TOP 1 cluster_id 
                                FROM MovieCentroids 
                                ORDER BY VECTOR_DISTANCE('euclidean', m.embedding, centroid_vector) ASC
                            ) c";

            using SqlCommand cmd = new(sql, conn);
            cmd.CommandTimeout = 120;
            int rows = await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Index built: {rows} movies assigned to clusters.");
        }

        /// <summary>
        /// Initializes the K-Means process by selecting K random vectors from the dataset 
        /// to serve as the initial cluster centroids.
        /// </summary>
        /// <param name="connString">The SQL Server connection string.</param>
        /// <param name="k">The number of clusters to create (default is 100).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task GenerateCentroidsAsync(string connString, int k = 100)
        {
            Console.WriteLine($"--- Generating {k} Initial Centroids ---");

            using SqlConnection conn = new(connString);
            await conn.OpenAsync();

            using SqlTransaction transaction = conn.BeginTransaction();

            try
            {
                // 1. Clear previous index data and reset movie assignments
                string clearSql = "DROP TABLE IF EXISTS MovieCentroids; UPDATE Movies SET cluster_id = NULL;";
                using (SqlCommand clearCmd = new(clearSql, conn, transaction))
                {
                    await clearCmd.ExecuteNonQueryAsync();
                }

                // Create the structure for the Centroid table
                string createTbSql = "CREATE TABLE MovieCentroids (\r\n    cluster_id INT PRIMARY KEY,\r\n    centroid_vector VECTOR(1536)\r\n);";
                using (SqlCommand createCmd = new(createTbSql, conn, transaction))
                {
                    await createCmd.ExecuteNonQueryAsync();
                }

                // 2. Pick K random movies and promote their embeddings to Centroids
                // NEWID() is used to ensure a random distribution of initial clusters
                string seedSql = $@"
                    INSERT INTO MovieCentroids (cluster_id, centroid_vector)
                    SELECT TOP (@K) 
                            ROW_NUMBER() OVER(ORDER BY NEWID()) as cluster_id, 
                            embedding
                    FROM Movies
                    WHERE embedding IS NOT NULL;";

                using (SqlCommand seedCmd = new(seedSql, conn, transaction))
                {
                    seedCmd.Parameters.AddWithValue("@K", k);
                    await seedCmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                Console.WriteLine($"Success: {k} Centroids created in MovieCentroids table.");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"Error during centroid generation: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs an Approximate Nearest Neighbor (ANN) search. 
        /// It first identifies the closest cluster centroid and then searches only within that cluster.
        /// </summary>
        /// <param name="connString">The SQL Server connection string.</param>
        /// <param name="queryVector">The high-dimensional vector representing the search query.</param>
        /// <returns>The total time taken for the search in milliseconds.</returns>
        public static async Task<float> SearchANNAsync(string connString, float[] queryVector)
        {
            string vectorJson = $"[{string.Join(",", queryVector.Select(f => f.ToString(CultureInfo.InvariantCulture)))}]";

            Stopwatch sw = Stopwatch.StartNew();

            using SqlConnection conn = new(connString);
            await conn.OpenAsync();

            // Two-step search: 1. Find the target cluster, 2. Search within that cluster
            string sql = @"
                DECLARE @TargetCluster INT;

                SELECT TOP 1 @TargetCluster = cluster_id
                FROM MovieCentroids
                ORDER BY VECTOR_DISTANCE('euclidean', centroid_vector, CAST(@Query AS VECTOR(1536))) ASC;

                SELECT TOP 5 title, genre, release_year, description
                FROM Movies
                WHERE cluster_id = @TargetCluster
                ORDER BY VECTOR_DISTANCE('euclidean', embedding, CAST(@Query AS VECTOR(1536))) ASC;";

            using SqlCommand cmd = new(sql, conn);
            cmd.Parameters.AddWithValue("@Query", vectorJson);

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            int count = 1;
            while (await reader.ReadAsync())
            {
                string title = reader.GetString(0);
                string genre = reader.GetString(1);
                int year = reader.GetInt32(2);
                string desc = reader.GetString(3);

                Console.WriteLine($"{count}. {title.ToUpper()} ({year})");
                Console.WriteLine($"   Genre: {genre}");

                string displayDesc = desc.Length > 150 ? desc[..147] + "..." : desc;
                Console.WriteLine($"   Description: {displayDesc}");
                Console.WriteLine(new string('-', 50));
                count++;
            }

            sw.Stop();
            float elapsedMs = (float)sw.Elapsed.TotalMilliseconds;

            Console.WriteLine($"--- Search Time: {elapsedMs:F2} ms ---");
            return elapsedMs;
        }

        /// <summary>
        /// Performs an Exact (Brute-Force) search by comparing the query vector 
        /// against every vector in the Movies table. Used as the Ground Truth for comparison.
        /// </summary>
        /// <param name="connString">The SQL Server connection string.</param>
        /// <param name="queryVector">The high-dimensional vector representing the search query.</param>
        /// <returns>The total time taken for the search in milliseconds.</returns>
        public static async Task<float> SearchExactAsync(string connString, float[] queryVector)
        {
            string vectorJson = $"[{string.Join(",", queryVector.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture)))}]";
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                string sql = @"
            SELECT TOP 5 title, genre, release_year, description
            FROM Movies
            ORDER BY VECTOR_DISTANCE('euclidean', embedding, CAST(@Query AS VECTOR(1536))) ASC;";

                using SqlCommand cmd = new(sql, conn);
                cmd.Parameters.AddWithValue("@Query", vectorJson);

                using SqlDataReader reader = await cmd.ExecuteReaderAsync();

                Console.WriteLine("\n==================================================");
                Console.WriteLine("🎬  EXACT SEARCH RESULTS (GROUND TRUTH)");
                Console.WriteLine("==================================================");

                int count = 1;
                while (await reader.ReadAsync())
                {
                    string title = reader.GetString(0);
                    string genre = reader.GetString(1);
                    int year = reader.GetInt32(2);
                    string desc = reader.GetString(3);

                    Console.WriteLine($"{count}. {title.ToUpper()} ({year})");
                    Console.WriteLine($"   🏷️ Genre: {genre}");

                    string shortDesc = desc.Length > 120 ? desc[..117] + "..." : desc;
                    Console.WriteLine($"   📝 Info:  {shortDesc}");
                    Console.WriteLine(new string('-', 50));
                    count++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Database Error: {ex.Message}");
            }

            sw.Stop();
            return (float)sw.Elapsed.TotalMilliseconds;
        }
    }
}