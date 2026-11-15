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
    public class NewznabApostropheSearchFixture
    {
        private readonly bool _torznab;
        private readonly BookMediaType _mediaType;
        private CapabilitiesProvider _capabilities;
        private NewznabRequestGenerator _generator;

        public NewznabApostropheSearchFixture(bool torznab, BookMediaType mediaType)
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
            _generator.Settings = new NewznabSettings { BaseUrl = "https://indexer.example", Categories = new[] { 3030, 7020 } };
            _generator.MaxPages = 1;
        }

        [Test]
        public void should_search_both_spellings_in_the_same_tier_when_the_title_has_an_apostrophe()
        {
            var tiers = Tiers(Criteria("Zoe's Tale", "John Scalzi"));

            Assert.That(tiers, Has.Count.EqualTo(4));
            Assert.That(tiers[0], Is.EqualTo(new[] { "book|John Scalzi|Zoe's Tale", "book|John Scalzi|Zoes Tale" }));
            Assert.That(tiers[1], Is.EqualTo(new[] { "book||Zoe's Tale", "book||Zoes Tale" }));
            Assert.That(tiers[2], Is.EqualTo(new[]
            {
                "Zoe's Tale John Scalzi",
                "Zoes Tale John Scalzi",
                "John Scalzi Zoe's Tale",
                "John Scalzi Zoes Tale"
            }));
            Assert.That(tiers[3], Is.EqualTo(new[] { "Zoe's Tale", "Zoes Tale" }));
        }

        [Test]
        public void should_escape_both_spellings()
        {
            var urls = Requests(Criteria("Zoe's Tale", "John Scalzi")).Select(r => r.Url.FullUri).ToList();

            Assert.That(urls, Has.Some.EndsWith("&q=Zoe%27s%20Tale"));
            Assert.That(urls, Has.Some.EndsWith("&q=Zoes%20Tale"));
        }

        [TestCase("Zoe’s Tale", "Zoe’s Tale")]
        [TestCase("Zoe`s Tale", "Zoe`s Tale")]
        public void should_treat_typographic_apostrophes_and_backticks_the_same_way(string title, string original)
        {
            var tiers = Tiers(Criteria(title, "John Scalzi"));

            Assert.That(tiers.Last(), Is.EqualTo(new[] { original, "Zoes Tale" }));
        }

        [Test]
        public void should_only_repeat_requests_that_carry_the_apostrophe_when_it_is_in_the_author()
        {
            var tiers = Tiers(Criteria("Dune", "Patrick O'Brian"));

            Assert.That(tiers, Has.Count.EqualTo(4));
            Assert.That(tiers[0], Is.EqualTo(new[] { "book|Patrick O'Brian|Dune", "book|Patrick OBrian|Dune" }));
            Assert.That(tiers[1], Is.EqualTo(new[] { "book||Dune" }));
            Assert.That(tiers[2], Is.EqualTo(new[]
            {
                "Dune Patrick O'Brian",
                "Dune Patrick OBrian",
                "Patrick O'Brian Dune",
                "Patrick OBrian Dune"
            }));
            Assert.That(tiers[3], Is.EqualTo(new[] { "Dune" }));
        }

        [Test]
        public void should_not_add_requests_when_nothing_has_an_apostrophe()
        {
            var tiers = Tiers(Criteria("Dune", "Frank Herbert"));

            Assert.That(tiers, Has.Count.EqualTo(4));
            Assert.That(tiers[0], Is.EqualTo(new[] { "book|Frank Herbert|Dune" }));
            Assert.That(tiers[1], Is.EqualTo(new[] { "book||Dune" }));
            Assert.That(tiers[2], Is.EqualTo(new[] { "Dune Frank Herbert", "Frank Herbert Dune" }));
            Assert.That(tiers[3], Is.EqualTo(new[] { "Dune" }));
        }

        [TestCase("Witches' Brew", "Witches' Brew", "Witches Brew")]
        [TestCase("'Salem's Lot", "'Salem's Lot", "Salems Lot")]
        [TestCase("Rock 'n' Roll", "Rock 'n' Roll", "Rock n Roll")]
        [TestCase("Rock ' Roll", "Rock ' Roll", "Rock Roll")]
        public void should_remove_apostrophes_wherever_they_sit_in_the_title(string title, string original, string withoutApostrophes)
        {
            var tiers = Tiers(Criteria(title, "Test Author"));

            Assert.That(tiers.Last(), Is.EqualTo(new[] { original, withoutApostrophes }));
        }

        [Test]
        public void should_not_send_an_empty_query_when_the_title_is_only_an_apostrophe()
        {
            var requests = Requests(Criteria("'", "Test Author"));

            Assert.That(requests, Has.Count.EqualTo(5));
            foreach (var request in requests)
            {
                var query = HttpUtility.ParseQueryString(request.Url.Query);
                Assert.That(query["q"] ?? query["title"], Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void should_never_send_the_same_request_twice()
        {
            foreach (var criteria in new[] { Criteria("Zoe's Tale", "John Scalzi"), Criteria("Dune", "Patrick O'Brian"), Criteria("Zoe's Tale", "Patrick O'Brian") })
            {
                var urls = Requests(criteria).Select(r => r.Url.FullUri).ToList();

                Assert.That(urls, Is.Unique);
            }
        }

        [Test]
        public void should_search_both_spellings_by_default()
        {
            Assert.That(new NewznabSettings().ApostropheSearch, Is.EqualTo((int)NewznabApostropheSearch.Both));
        }

        [Test]
        public void should_keep_the_apostrophe_when_set_to_as_written()
        {
            _generator.Settings.ApostropheSearch = (int)NewznabApostropheSearch.AsWritten;

            var tiers = Tiers(Criteria("Zoe's Tale", "Patrick O'Brian"));

            Assert.That(tiers, Has.Count.EqualTo(4));
            Assert.That(tiers[0], Is.EqualTo(new[] { "book|Patrick O'Brian|Zoe's Tale" }));
            Assert.That(tiers[1], Is.EqualTo(new[] { "book||Zoe's Tale" }));
            Assert.That(tiers[2], Is.EqualTo(new[] { "Zoe's Tale Patrick O'Brian", "Patrick O'Brian Zoe's Tale" }));
            Assert.That(tiers[3], Is.EqualTo(new[] { "Zoe's Tale" }));
        }

        [Test]
        public void should_drop_the_apostrophe_when_set_to_removed()
        {
            _generator.Settings.ApostropheSearch = (int)NewznabApostropheSearch.Removed;

            var tiers = Tiers(Criteria("Zoe's Tale", "Patrick O'Brian"));

            Assert.That(tiers, Has.Count.EqualTo(4));
            Assert.That(tiers[0], Is.EqualTo(new[] { "book|Patrick OBrian|Zoes Tale" }));
            Assert.That(tiers[1], Is.EqualTo(new[] { "book||Zoes Tale" }));
            Assert.That(tiers[2], Is.EqualTo(new[] { "Zoes Tale Patrick OBrian", "Patrick OBrian Zoes Tale" }));
            Assert.That(tiers[3], Is.EqualTo(new[] { "Zoes Tale" }));
        }

        [Test]
        public void should_search_as_written_when_removing_apostrophes_would_leave_nothing()
        {
            _generator.Settings.ApostropheSearch = (int)NewznabApostropheSearch.Removed;

            var tiers = Tiers(Criteria("'", "Test Author"));

            Assert.That(tiers.Last(), Is.EqualTo(new[] { "'" }));
        }

        [Test]
        public void should_not_read_the_title_or_author_when_the_indexer_supports_no_search()
        {
            _capabilities.Capabilities = new NewznabCapabilities { SupportedSearchParameters = null, SupportedBookSearchParameters = null };

            Assert.That(Requests(new BookSearchCriteria { Books = new List<Book> { new Book { MediaType = _mediaType } } }), Is.Empty);
        }

        [Test]
        public void should_search_both_spellings_on_indexers_without_book_search()
        {
            _capabilities.Capabilities = new NewznabCapabilities { SupportedSearchParameters = new[] { "q" }, SupportedBookSearchParameters = null };

            var tiers = Tiers(Criteria("Zoe's Tale", "John Scalzi"));

            Assert.That(tiers, Has.Count.EqualTo(2));
            Assert.That(tiers[0], Is.EqualTo(new[]
            {
                "Zoe's Tale John Scalzi",
                "Zoes Tale John Scalzi",
                "John Scalzi Zoe's Tale",
                "John Scalzi Zoes Tale"
            }));
            Assert.That(tiers[1], Is.EqualTo(new[] { "Zoe's Tale", "Zoes Tale" }));
        }

        [Test]
        public void should_leave_the_pinned_narrator_request_as_it_is()
        {
            var criteria = Criteria("Zoe's Tale", "John Scalzi");
            criteria.Books = new List<Book>
            {
                new Book
                {
                    MediaType = BookMediaType.Audiobook,
                    AnyEditionOk = false,
                    Editions = new List<Edition>
                    {
                        new Edition { ReadingFormatId = 2, Monitored = true, Narrator = "Tavia Gilbert" }
                    }
                }
            };

            var tiers = Tiers(criteria);

            Assert.That(tiers[0], Is.EqualTo(new[] { "Zoe's Tale John Scalzi Tavia Gilbert" }));
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

        private List<IndexerRequest> Requests(BookSearchCriteria criteria)
        {
            return _generator.GetSearchRequests(criteria).GetAllTiers().SelectMany(page => page).ToList();
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
