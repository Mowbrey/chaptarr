using System.Collections.Generic;
using System.Linq;
using System.Web;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace Chaptarr.Core.Test.Indexers
{
    [TestFixture(false, BookMediaType.Ebook)]
    [TestFixture(true, BookMediaType.Ebook)]
    [TestFixture(false, BookMediaType.Audiobook)]
    [TestFixture(true, BookMediaType.Audiobook)]
    public class NewznabPlainWordsSearchFixture
    {
        private readonly bool _torznab;
        private readonly BookMediaType _mediaType;
        private CapabilitiesProvider _capabilities;
        private NewznabRequestGenerator _generator;

        public NewznabPlainWordsSearchFixture(bool torznab, BookMediaType mediaType)
        {
            _torznab = torznab;
            _mediaType = mediaType;
        }

        [SetUp]
        public void SetUp()
        {
            _capabilities = new CapabilitiesProvider();
            var logger = LogManager.CreateNullLogger();
            _generator = _torznab
                ? new TorznabRequestGenerator(_capabilities, logger, "Selected Indexer")
                : new NewznabRequestGenerator(_capabilities, logger, "Selected Indexer");
            _generator.Settings = new NewznabSettings
            {
                BaseUrl = "https://indexer.example",
                Categories = new[] { 3030, 7020 }
            };
            _generator.MaxPages = 1;
        }

        [Test]
        public void should_add_a_last_tier_of_plain_words_when_the_title_has_punctuation()
        {
            var tiers = Tiers(Criteria("Zoe's Tale", "John Scalzi"));

            Assert.That(tiers, Has.Count.EqualTo(5));
            Assert.That(tiers[0], Is.EqualTo(new[] { "book|John Scalzi|Zoe's Tale" }));
            Assert.That(tiers[1], Is.EqualTo(new[] { "book||Zoe's Tale" }));
            Assert.That(tiers[2], Is.EqualTo(new[] { "Zoe's Tale John Scalzi", "John Scalzi Zoe's Tale" }));
            Assert.That(tiers[3], Is.EqualTo(new[] { "Zoe's Tale" }));
            Assert.That(tiers[4], Is.EqualTo(new[] { "Tale John Scalzi" }));
        }

        [TestCase("A Cook's Tour", "Anthony Bourdain", "Tour Anthony Bourdain")]
        [TestCase("Dead Man's Walk", "Larry McMurtry", "Dead Walk Larry McMurtry")]
        [TestCase("Ten Nights' Dreams", "Natsume Sōseki", "Ten Nights Dreams Natsume Soseki")]
        [TestCase("The Women of the Cousins' War: The Duchess, the Queen, and the King's Mother", "Philippa Gregory", "Women Cousins War Philippa Gregory")]
        [TestCase("Interview with the Vampire", "Anne Rice", "Interview with Vampire Anne Rice")]
        [TestCase("Why I Cook", "Tom Colicchio", "Why Cook Tom Colicchio")]
        [TestCase("The Far Pavilions", "M.M. Kaye", "Far Pavilions Kaye")]
        [TestCase("Against All Things Ending", "Stephen R. Donaldson", "Against All Things Ending Stephen Donaldson")]
        [TestCase("The Hanged Man’s Song", "John Sandford", "Hanged Song John Sandford")]
        [TestCase("Spider-Man Noir", "David Hine", "Noir David Hine")]
        [TestCase("Rock and Roll", "Tom Stoppard", "Rock Roll Tom Stoppard")]
        public void should_search_only_the_words_every_indexer_reads_the_same_way(string title, string author, string expected)
        {
            var tiers = Tiers(Criteria(title, author));

            Assert.That(tiers.Last(), Is.EqualTo(new[] { expected }));
        }

        [TestCase("Starter Villain", "John Scalzi")]
        [TestCase("The Collapsing Empire", "John Scalzi")]
        [TestCase("Rock & Roll", "Tom Stoppard")]
        public void should_not_add_a_tier_when_the_plain_words_match_the_first_search(string title, string author)
        {
            var tiers = Tiers(Criteria(title, author));

            Assert.That(tiers, Has.Count.EqualTo(4));
        }

        [TestCase("Catch-22", "Joseph Heller")]
        [TestCase("'", "Test Author")]
        [TestCase("V", "Thomas Pynchon")]
        public void should_not_add_a_tier_when_no_plain_title_word_is_left(string title, string author)
        {
            var tiers = Tiers(Criteria(title, author));

            Assert.That(tiers, Has.Count.EqualTo(4));
            Assert.That(tiers.SelectMany(tier => tier), Has.None.EqualTo(author));
        }

        [Test]
        public void should_send_the_plain_words_as_one_escaped_search_in_the_requested_category()
        {
            var request = _generator.GetSearchRequests(Criteria("Zoe's Tale", "John Scalzi")).GetAllTiers().Last().Single();
            var category = _mediaType == BookMediaType.Audiobook ? "3030" : "7020";

            Assert.That(request.Url.FullUri, Does.Contain($"?t=search&cat={category}&"));
            Assert.That(request.Url.FullUri, Does.EndWith("&q=Tale%20John%20Scalzi"));
        }

        [Test]
        public void should_add_the_plain_words_tier_on_indexers_without_book_search()
        {
            _capabilities.Capabilities = new NewznabCapabilities { SupportedSearchParameters = new[] { "q" }, SupportedBookSearchParameters = null };

            var tiers = Tiers(Criteria("Zoe's Tale", "John Scalzi"));

            Assert.That(tiers, Has.Count.EqualTo(3));
            Assert.That(tiers[2], Is.EqualTo(new[] { "Tale John Scalzi" }));
        }

        [Test]
        public void should_not_add_the_plain_words_tier_on_indexers_without_text_search()
        {
            _capabilities.Capabilities = new NewznabCapabilities { SupportedSearchParameters = null, SupportedBookSearchParameters = new[] { "author", "title" } };

            var tiers = Tiers(Criteria("Zoe's Tale", "John Scalzi"));

            Assert.That(tiers, Has.Count.EqualTo(2));
            Assert.That(tiers.SelectMany(tier => tier), Has.All.StartsWith("book|"));
        }

        private BookSearchCriteria Criteria(string title, string author)
        {
            return new BookSearchCriteria
            {
                Author = new Author { Name = author },
                BookTitle = title,
                Books = new List<Book> { new Book { MediaType = _mediaType } }
            };
        }

        private List<List<string>> Tiers(BookSearchCriteria criteria)
        {
            var chain = _generator.GetSearchRequests(criteria);

            return Enumerable.Range(0, chain.Tiers)
                .Select(tier => chain.GetTier(tier).SelectMany(page => page).Select(SearchText).ToList())
                .ToList();
        }

        private static string SearchText(IndexerRequest request)
        {
            var query = HttpUtility.ParseQueryString(request.Url.Query);

            return query["q"] ?? $"{query["t"]}|{query["author"]}|{query["title"]}";
        }

        private sealed class CapabilitiesProvider : INewznabCapabilitiesProvider
        {
            public NewznabCapabilities Capabilities { get; set; } = new NewznabCapabilities();

            public NewznabCapabilities GetCapabilities(NewznabSettings settings, int? proxyId, bool forceRefresh = false)
            {
                return Capabilities;
            }
        }
    }
}
