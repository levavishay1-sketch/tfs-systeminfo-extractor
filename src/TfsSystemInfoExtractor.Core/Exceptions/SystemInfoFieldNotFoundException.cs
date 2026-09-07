namespace TfsSystemInfoExtractor.Core.Exceptions
{
    /// <summary>No field with the configured display name exists in the collection's field catalogue.</summary>
    public sealed class SystemInfoFieldNotFoundException : TfsExtractorException
    {
        public SystemInfoFieldNotFoundException(string displayName)
            : base($"Could not find a field whose display name is \"{displayName}\" in the TFS field definitions. " +
                   "Verify the field exists and the display name matches exactly.")
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
    }
}
