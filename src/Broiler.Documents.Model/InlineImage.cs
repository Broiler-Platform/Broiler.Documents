using System;

namespace Broiler.Documents.Model;

/// <summary>
/// An image embedded in a document. The model has no separate inline-object
/// shape, so an image lives on the <see cref="InlineStyle"/> of a run whose text
/// is the single object replacement character <see cref="Placeholder"/>: the
/// image occupies exactly one character position, which is what makes carets,
/// selections, deletion, and paragraph splitting work on it without a second set
/// of rules.
/// </summary>
/// <remarks>
/// Instances are compared by reference, so two runs carrying the same image
/// object merge and two runs carrying equal bytes do not. That keeps a document
/// that repeats one picture cheap without forcing a byte comparison into every
/// run-normalization pass.
/// </remarks>
public sealed class InlineImage
{
    /// <summary>
    /// U+FFFC OBJECT REPLACEMENT CHARACTER — the one character an image run
    /// holds. Text is never stored for an image; alternative text lives in
    /// <see cref="AltText"/>.
    /// </summary>
    public const char Placeholder = '\uFFFC';

    /// <summary><see cref="Placeholder"/> as a string, for insertion calls.</summary>
    public const string PlaceholderText = "\uFFFC";

    public InlineImage(
        ReadOnlyMemory<byte> data,
        string contentType,
        double width,
        double height,
        string? altText = null,
        string? name = null)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("An inline image needs a content type.", nameof(contentType));
        if (width < 0 || double.IsNaN(width))
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0 || double.IsNaN(height))
            throw new ArgumentOutOfRangeException(nameof(height));

        Data = data;
        ContentType = contentType;
        Width = width;
        Height = height;
        AltText = altText ?? string.Empty;
        Name = string.IsNullOrWhiteSpace(name) ? "image" : name;
    }

    /// <summary>The encoded image bytes, in the encoding named by <see cref="ContentType"/>.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>The IANA media type of <see cref="Data"/>, for example <c>image/png</c>.</summary>
    public string ContentType { get; }

    /// <summary>
    /// Display width in the same logical units as <see cref="InlineStyle.FontSize"/>
    /// (points at 1:1 scale). Zero means "use the encoded pixel size".
    /// </summary>
    public double Width { get; }

    /// <summary>Display height, in the units described by <see cref="Width"/>.</summary>
    public double Height { get; }

    /// <summary>Alternative text, or the empty string when the source gave none.</summary>
    public string AltText { get; }

    /// <summary>A short name for the image, used to name the part a writer emits.</summary>
    public string Name { get; }

    /// <summary>True when the source stated a display size rather than leaving it implied.</summary>
    public bool HasExplicitSize => Width > 0 && Height > 0;

    /// <summary>Returns the same image drawn at a different size.</summary>
    public InlineImage WithSize(double width, double height) =>
        new(Data, ContentType, width, height, AltText, Name);

    /// <summary>Returns the same image with different alternative text.</summary>
    public InlineImage WithAltText(string? altText) =>
        new(Data, ContentType, Width, Height, altText, Name);
}
