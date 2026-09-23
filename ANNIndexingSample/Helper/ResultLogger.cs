namespace ANNIndexingSample.Helper
{
    /// <summary>
    /// Provides utility methods for persisting search performance metrics to the local file system.
    /// This helps in analyzing the speed gains of ANN versus Exact search over multiple trials.
    /// </summary>
    internal static class ResultLogger
    {
        private const string FolderPath = "Output";
        private const string FileName = "records.txt";
        private static readonly string FullPath = Path.Combine(FolderPath, FileName);

        /// <summary>
        /// Appends a series of search execution times to a local log file.
        /// Automatically handles directory creation and formats the output for readability.
        /// </summary>
        /// <param name="query">The search term or prompt used for the query.</param>
        /// <param name="times">A list of elapsed times (in milliseconds) from multiple search trials.</param>
        /// <returns>A task representing the asynchronous save operation.</returns>
        public static async Task SaveSearchRecordAsync(string query, List<float> times)
        {
            // Validation: Don't attempt to log if there is no data
            if (times == null || times.Count == 0) return;

            // Ensure the 'Output' directory exists to avoid DirectoryNotFoundException
            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }

            // Create a formatted list of strings for the log: e.g., "1: 12.45 ms"
            var entry = times.Select((t, i) => $"{i + 1}: {t:F2} ms").ToList();

            // Insert metadata at the top of the entry for context
            entry.Insert(0, $"--- Query: {query} ---");

            // Add a trailing empty line to visually separate different search sessions in the text file
            entry.Add("");

            // Perform the asynchronous file write
            await File.AppendAllLinesAsync(FullPath, entry);

            Console.WriteLine($"\n[Log] {times.Count} trials saved to {FullPath}");
        }
    }
}