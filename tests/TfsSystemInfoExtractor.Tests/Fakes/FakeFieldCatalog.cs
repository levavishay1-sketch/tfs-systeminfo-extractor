using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Tests.Fakes
{
    internal sealed class FakeFieldCatalog : IFieldCatalog
    {
        private readonly List<FieldDefinition> _fields = new List<FieldDefinition>();

        public int Calls { get; private set; }

        public FakeFieldCatalog Add(string referenceName, string displayName, string? type = null)
        {
            _fields.Add(new FieldDefinition(referenceName, displayName, type));
            return this;
        }

        public Task<IReadOnlyList<FieldDefinition>> GetFieldsAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<FieldDefinition>>(_fields);
        }
    }

    internal sealed class FixedSystemInfoFieldResolver : ISystemInfoFieldResolver
    {
        private readonly string _referenceName;

        public FixedSystemInfoFieldResolver(string referenceName) => _referenceName = referenceName;

        public Task<string> ResolveReferenceNameAsync(CancellationToken cancellationToken) => Task.FromResult(_referenceName);
    }
}
