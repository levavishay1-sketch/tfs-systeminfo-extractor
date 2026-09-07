namespace TfsSystemInfoExtractor.Core.Extraction
{
    /// <summary>Tuning knobs for the traversal itself (as opposed to how TFS is reached).</summary>
    public sealed class ExtractionOptions
    {
        public const string SectionName = "Extraction";

        /// <summary>
        /// Hard ceiling on hierarchy depth, a safety net against pathological or
        /// self-referential link graphs. The de-duplication guard already prevents
        /// genuine cycles; this simply bounds the worst case.
        /// </summary>
        public int MaxDepth { get; set; } = 50;
    }
}
