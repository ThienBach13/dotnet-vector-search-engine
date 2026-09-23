using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ANNIndexingSample.Entities
{
    /// <summary>
    /// Represents a movie record from the database. 
    /// This entity is used to store and pass metadata during the search and indexing process.
    /// </summary>
    internal class MovieData
    {
        /// <summary>
        /// Gets or sets the unique primary identifier for the movie.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the official title of the movie.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the primary genre of the movie (e.g., Action, Sci-Fi).
        /// </summary>
        public string Genre { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the release year of the movie.
        /// </summary>
        public int Year { get; set; }

        /// <summary>
        /// Gets or sets a brief plot summary or description. 
        /// In ANN contexts, this is often the text used to generate the embedding vector.
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }
}