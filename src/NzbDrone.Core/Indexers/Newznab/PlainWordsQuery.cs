using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Utilities;

namespace NzbDrone.Core.Indexers.Newznab
{
    // Indexers disagree on how to index a word with punctuation inside it: "Zoe's" is stored as
    // "zoe's", "zoes" or "zoe s" depending on the site, so no single spelling finds every release.
    // This query leaves such words out and keeps only the words every indexer reads the same way.
    internal static class PlainWordsQuery
    {
        private static readonly HashSet<string> FunctionWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "of", "the"
        };

        // Returns null when there is nothing to add to the searches already made.
        public static string Build(BookSearchCriteria criteria)
        {
            var author = criteria.Author?.Name;
            var title = BookSearchCriteria.GetMainSearchTitle(criteria.BookTitle, author);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author))
            {
                return null;
            }

            var titleWords = PlainWords(title).Where(word => !FunctionWords.Contains(word)).ToList();
            var authorWords = PlainWords(author).ToList();
            if (titleWords.Count == 0 || authorWords.Count == 0)
            {
                return null;
            }

            var query = string.Join("+", titleWords.Concat(authorWords));
            var alreadySearched = $"{criteria.BookQuery}+{criteria.AuthorQuery}";

            return string.Equals(query, alreadySearched, StringComparison.OrdinalIgnoreCase) ? null : query;
        }

        private static IEnumerable<string> PlainWords(string text)
        {
            foreach (var raw in text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                var word = raw.Trim(raw.Where(c => !char.IsLetterOrDigit(c)).ToArray());

                // One-character words are mostly initials, which releases write in too many ways.
                if (word.Length > 1 && word.All(char.IsLetterOrDigit))
                {
                    yield return UnicodeComparisonNormalizer.NormalizeText(word, preserveCase: true);
                }
            }
        }
    }
}
