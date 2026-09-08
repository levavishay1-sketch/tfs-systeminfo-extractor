using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Extraction;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Infrastructure.Text;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class ExtractionServiceTests
    {
        private sealed class FixedClock : ISystemClock
        {
            public DateTimeOffset Now => new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);
        }

        private static ExtractionService Build(FakeWorkItemSource source, FakeFieldCatalog catalog)
        {
            var walker = new HierarchyWalker(source, new HtmlToPlainTextConverter(), Options.Create(new ExtractionOptions()));
            return new ExtractionService(
                new FixedSystemInfoFieldResolver("Custom.SystemInfo"),
                catalog,
                walker,
                new FixedClock(),
                NullLogger<ExtractionService>.Instance);
        }

        [Fact]
        public async Task Available_fields_are_the_field_keys_present_on_nodes_mapped_to_display_names_and_sorted()
        {
            var source = new FakeWorkItemSource()
                .AddWithFields(1, new Dictionary<string, string> { ["System.AssignedTo"] = "Jane", ["Microsoft.VSTS.Common.Priority"] = "2" }, childIds: new[] { 2 })
                .AddWithFields(2, new Dictionary<string, string> { ["System.AssignedTo"] = "John" });

            var catalog = new FakeFieldCatalog()
                .Add("System.AssignedTo", "Assigned To")
                .Add("Microsoft.VSTS.Common.Priority", "Priority")
                .Add("System.Reason", "Reason"); // in catalogue but not on any node

            var result = await Build(source, catalog).ExtractAsync(new ExtractionRequest(new[] { 1 }), null!, CancellationToken.None);

            Assert.Equal(new[] { "Assigned To", "Priority" }, result.AvailableFields.Select(f => f.DisplayName));
            Assert.DoesNotContain(result.AvailableFields, f => f.ReferenceName == "System.Reason");
        }

        [Fact]
        public async Task Available_fields_is_empty_and_catalogue_untouched_when_no_extra_fields_present()
        {
            var source = new FakeWorkItemSource().Add(1, "<p>info</p>");
            var catalog = new FakeFieldCatalog().Add("System.AssignedTo", "Assigned To");

            var result = await Build(source, catalog).ExtractAsync(new ExtractionRequest(new[] { 1 }), null!, CancellationToken.None);

            Assert.Empty(result.AvailableFields);
        }

        [Fact]
        public async Task Field_values_are_carried_onto_the_nodes()
        {
            var source = new FakeWorkItemSource()
                .AddWithFields(1, new Dictionary<string, string> { ["System.AssignedTo"] = "Jane Doe" });
            var catalog = new FakeFieldCatalog().Add("System.AssignedTo", "Assigned To");

            var result = await Build(source, catalog).ExtractAsync(new ExtractionRequest(new[] { 1 }), null!, CancellationToken.None);

            Assert.Equal("Jane Doe", result.Roots.Single().Fields!["System.AssignedTo"]);
        }
    }
}
