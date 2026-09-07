namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>Converts the rich-text (HTML) value of a TFS field to trimmed plain text.</summary>
    public interface IHtmlToText
    {
        string Convert(string? html);
    }
}
