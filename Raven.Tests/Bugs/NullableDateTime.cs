using System;
using Raven.Abstractions;
using Raven.Abstractions.Indexing;
using Raven.Client;
using Raven.Client.Indexes;
using Raven.Client.Linq;
using Raven.Database.Server;
using Raven.Json.Linq;
using Raven.Tests.Common;

using Xunit;
using System.Linq;

namespace Raven.Tests.Bugs
{
    public class NullableDateTime : RavenTest
    {
        [Fact]
        public void WillNotIncludeItemsWithNullDate()
        {
            using (var store = NewDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new WithNullableDateTime());
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var withNullableDateTimes = session.Query<WithNullableDateTime>()
                        .Customize(x => x.WaitForNonStaleResults())
                        .Where(x => x.CreatedAt > new DateTime(2000, 1, 1) && x.CreatedAt != null)
                        .ToList();
                    Assert.Empty(withNullableDateTimes);
                }
            }
        }

        public class WithNullableDateTime
        {
            public string Id { get; set; }
            public DateTime? CreatedAt { get; set; }
        }

        public class Doc
        {
            public string Id { get; set; }
            public DateTime? Date { get; set; }
        }

        public class DocSummary
        {
            public string Id { get; set; }
            public DateTime? MaxDate { get; set; }
        }

        public class UnsetDocs : AbstractIndexCreationTask<Doc, DocSummary>
        {
            public UnsetDocs()
            {
                Map = docs =>
                        from doc in docs
                        select new
                        {
                            doc.Id,
                            MaxDate = doc.Date
                        };
                Store(x => x.MaxDate, FieldStorage.Yes);
            }
        }

        [Fact]
        public void CanLoadFromIndex()
        {
            using (var documentStore = NewDocumentStore())
            {
                using (IDocumentSession session = documentStore.OpenSession())
                {
                    new UnsetDocs().Execute(documentStore);
                    session.Store(new Doc { Id = "test/doc1", Date = SystemTime.UtcNow });
                    session.Store(new Doc { Id = "test/doc2", Date = null });
                    session.SaveChanges();

                }

                using (var session = documentStore.OpenSession())
                {
                    session
                        .Query<Doc, UnsetDocs>()
                        .Customize(x => x.WaitForNonStaleResults())
                        .ProjectFromIndexFieldsInto<DocSummary>()
                        .ToArray();
                }
            }

        }

        [Fact]
        public void CanLoadFromIndex_Remote()
        {
            var path = NewDataPath();

            using (IDocumentStore documentStore = NewRemoteDocumentStore(dataDirectory: path))
            {
                using (IDocumentSession session = documentStore.OpenSession())
                {
                    new UnsetDocs().Execute(documentStore);
                    session.Store(new Doc
                    {
                        Id = "test/doc1",
                        Date = SystemTime.UtcNow
                    });
                    session.Store(new Doc { Id = "test/doc2", Date = null });
                    session.SaveChanges();

                }

                using (var session = documentStore.OpenSession())
                {
                    session
                        .Query<Doc, UnsetDocs>()
                        .Customize(x => x.WaitForNonStaleResults())
                        .ProjectFromIndexFieldsInto<DocSummary>()
                        .ToArray();
                }
            }
        }

        public class DocsByDate : AbstractIndexCreationTask<Doc>
        {
            public DocsByDate()
            {
                Map = docs =>
                            from doc in docs
                            let datetime = doc.Date ?? DateTime.MinValue
                            select new
                            {
                                Date = datetime
                            };

                Store(x => x.Date, FieldStorage.Yes);
            }
        }

        [Fact]
        public void CanWorkWithNullableDateTime()
        {
            using (var store = NewDocumentStore())
            {
                new DocsByDate().Execute(store);

                using (var session = store.OpenSession())
                {
                    var items = new[]
                                    {
                                        new Doc {Date = null},
                                        new Doc {Date = SystemTime.UtcNow},
                                    };
                    foreach (var item in items)
                        session.Store(item);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var items = session
                        .Query<Doc, DocsByDate>()
                        .Customize(x => x.WaitForNonStaleResultsAsOfLastWrite())
                        .ToArray();


                    Assert.Equal(2, items.Length);
                }
            }
        }
    }
}
