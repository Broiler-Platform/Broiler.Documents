using System;
using System.Diagnostics.CodeAnalysis;
using Broiler.Graphics;

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
/// <para>
/// Instances are compared by reference, so two runs carrying the same image
/// object merge and two runs carrying equal bytes do not. That keeps a document
/// that repeats one picture cheap without forcing a byte comparison into every
/// run-normalization pass.
/// </para>
/// <para>
/// <strong>The payload is a <see cref="BImageResource"/>.</strong> It may hold
/// encoded bytes or decoded samples, and the difference is visible rather than
/// hidden: a picture read from a DOCX part has bytes a writer can copy through
/// untouched, and a picture recovered from inside a PDF has only pixels. Code
/// that needs bytes asks for them and handles their absence.
/// </para>
/// <para>
/// <strong>The <see cref="ResourceId"/> is not authorization.</strong> It names
/// an entry in the conversion context that produced this image, and a writer
/// checks that entry before emitting anything. An image whose id belongs to
/// another conversion — pasted, merged, deserialized — finds no entry in the
/// destination and its resource has to be decided on again.
/// </para>
/// <para>
/// <strong>Dimensions are points, and null means auto.</strong> Never zero, never
/// NaN, never infinite: a sentinel that is also a legal value is a bug waiting for
/// a document that means it. Both null takes the resource's intrinsic pixels at
/// 96 per inch; one null preserves the intrinsic aspect ratio; and a resource
/// with no intrinsic size cannot resolve either, which is a reportable condition
/// rather than a default.
/// </para>
/// </remarks>
/// <summary>The shape a picture is shown in, when it is not shown as a rectangle.</summary>
public enum ImageMask
{
    /// <summary>The whole rectangle, which is what a picture is unless it says otherwise.</summary>
    None,

    /// <summary>An ellipse inscribed in the picture's box — the round portrait every CV template uses.</summary>
    Ellipse,
}

/// <summary>
/// How a picture is presented: the part of the source shown, and the shape it is
/// shown in. Separate from the image itself because it says nothing about the
/// bytes — two pictures can share one payload and crop it differently.
/// </summary>
/// <remarks>
/// The crop is stated as fractions of the source, matching what every format
/// states it as and surviving a resize the way a pixel offset would not. Each
/// edge is how much of the source that side gives up: 0.1 on the left drops the
/// leftmost tenth. Negative values, which some formats allow to pad a picture
/// out, are not represented and are clamped away.
/// </remarks>
public sealed record ImagePresentation
{
    /// <summary>The whole picture, as a rectangle.</summary>
    public static ImagePresentation Default { get; } = new();

    public double CropLeft { get; init; }

    public double CropTop { get; init; }

    public double CropRight { get; init; }

    public double CropBottom { get; init; }

    public ImageMask Mask { get; init; }

    /// <summary>True when this asks for nothing the default does not already do.</summary>
    public bool IsDefault =>
        Mask == ImageMask.None && !HasCrop;

    /// <summary>True when any edge gives up part of the source.</summary>
    public bool HasCrop =>
        CropLeft > 0 || CropTop > 0 || CropRight > 0 || CropBottom > 0;

    /// <summary>
    /// The crop as a source rectangle over a picture of this size, clamped so the
    /// rectangle always has area: a document stating crops that meet or cross
    /// leaves nothing to draw, and drawing nothing loses the picture entirely.
    /// </summary>
    public BRect SourceRect(double width, double height)
    {
        double left = Clamp(CropLeft);
        double top = Clamp(CropTop);
        double right = Clamp(CropRight);
        double bottom = Clamp(CropBottom);

        if (left + right >= 1)
            (left, right) = (0, 0);
        if (top + bottom >= 1)
            (top, bottom) = (0, 0);

        return new BRect(
            width * left,
            height * top,
            Math.Max(1, width * (1 - left - right)),
            Math.Max(1, height * (1 - top - bottom)));
    }

    /// <summary>
    /// The picture as this presentation shows it: cropped to the part it uses,
    /// then masked to its shape. Returns <paramref name="source"/> itself when
    /// the presentation asks for neither, so the ordinary picture allocates
    /// nothing and the caller can compare by reference to know whether it owns a
    /// second bitmap to dispose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is not interchangeable. Cropping selects part of the source and
    /// masking shapes what is drawn, so the ellipse has to be inscribed in the
    /// <em>cropped</em> box: masking first and cropping after would draw a slice
    /// of an ellipse rather than an ellipse.
    /// </para>
    /// <para>
    /// The edge is sampled rather than tested once per pixel. A hard in-or-out
    /// test gives a portrait a visibly stepped rim, which is the one place the
    /// shape is actually looked at; sixteen samples per pixel cost one pass over
    /// an image that is decoded once and cached.
    /// </para>
    /// <para>
    /// Alpha is straight rather than premultiplied here, so shaping the picture
    /// is a matter of scaling that channel alone and the colour channels stay as
    /// they were. The backends premultiply when they upload.
    /// </para>
    /// </remarks>
    public BBitmap Apply(BBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (IsDefault)
            return source;

        BRect box = SourceRect(source.Width, source.Height);
        int left = Math.Clamp((int)Math.Round(box.Left), 0, Math.Max(0, source.Width - 1));
        int top = Math.Clamp((int)Math.Round(box.Top), 0, Math.Max(0, source.Height - 1));
        int width = Math.Clamp((int)Math.Round(box.Width), 1, source.Width - left);
        int height = Math.Clamp((int)Math.Round(box.Height), 1, source.Height - top);

        ReadOnlySpan<byte> from = source.Rgba;
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = (((y + top) * source.Width) + left) * 4;
            from.Slice(sourceRow, width * 4).CopyTo(rgba.AsSpan(y * width * 4));
        }

        if (Mask == ImageMask.Ellipse)
            MaskToEllipse(rgba, width, height);

        return new BBitmap(width, height, rgba, takeOwnership: true);
    }

    /// <summary>Scales alpha by how much of each pixel the inscribed ellipse covers.</summary>
    private static void MaskToEllipse(byte[] rgba, int width, int height)
    {
        const int Samples = 4;
        double radiusX = width / 2d;
        double radiusY = height / 2d;
        if (radiusX <= 0 || radiusY <= 0)
            return;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int covered = 0;
                for (int sy = 0; sy < Samples; sy++)
                {
                    double py = ((y + ((sy + 0.5) / Samples)) - radiusY) / radiusY;
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        double px = ((x + ((sx + 0.5) / Samples)) - radiusX) / radiusX;
                        if ((px * px) + (py * py) <= 1)
                            covered++;
                    }
                }

                if (covered == Samples * Samples)
                    continue;

                int alpha = ((y * width) + x) * 4 + 3;
                rgba[alpha] = (byte)(rgba[alpha] * covered / (Samples * Samples));
            }
        }
    }

    private static double Clamp(double value) =>
        double.IsFinite(value) && value > 0 ? Math.Min(value, 1) : 0;
}

public sealed class InlineImage
{
    /// <summary>
    /// U+FFFC OBJECT REPLACEMENT CHARACTER — the one character an image run
    /// holds. Text is never stored for an image; alternative text lives in
    /// <see cref="AltText"/>.
    /// </summary>
    public const char Placeholder = '￼';

    /// <summary><see cref="Placeholder"/> as a string, for insertion calls.</summary>
    public const string PlaceholderText = "￼";

    /// <summary>
    /// The conversion from intrinsic pixels to model points, fixed rather than
    /// resolved from a device: a document's natural size must not depend on the
    /// screen it was opened on.
    /// </summary>
    public const double PixelsPerInch = 96.0;

    /// <summary>Points per inch, the unit every model dimension is in.</summary>
    public const double PointsPerInch = 72.0;

    public InlineImage(
        BImageResource resource,
        DocumentResourceId resourceId = default,
        double? width = null,
        double? height = null,
        string? altText = null,
        string? name = null,
        ImagePresentation? presentation = null)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        ResourceId = resourceId;
        Width = Validate(width, nameof(width));
        Height = Validate(height, nameof(height));
        AltText = altText ?? string.Empty;
        Name = string.IsNullOrWhiteSpace(name) ? "image" : name;
        Presentation = presentation ?? ImagePresentation.Default;
    }

    /// <summary>
    /// Creates an image from encoded bytes and their media type, inspecting the
    /// payload for its intrinsic size.
    /// </summary>
    /// <remarks>
    /// The compatibility shape, kept because most callers have exactly this and
    /// nothing more. A zero <paramref name="width"/> or <paramref name="height"/>
    /// is read as "auto" rather than as a size, which is what it meant before
    /// dimensions became nullable; pass null in new code and say so.
    /// </remarks>
    public InlineImage(
        ReadOnlyMemory<byte> data,
        string contentType,
        double width,
        double height,
        string? altText = null,
        string? name = null,
        ImagePresentation? presentation = null)
        : this(
            BImageResource.FromEncoded(data, Required(contentType)),
            default,
            width == 0 ? null : width,
            height == 0 ? null : height,
            altText,
            name,
            presentation)
    {
    }

    /// <summary>The image itself, encoded or decoded.</summary>
    public BImageResource Resource { get; }

    /// <summary>
    /// The part of the picture shown and the shape it is shown in. Never null;
    /// a picture that states nothing carries <see cref="ImagePresentation.Default"/>.
    /// </summary>
    public ImagePresentation Presentation { get; }

    /// <summary>
    /// This image's entry in the conversion context that produced it, or
    /// <see cref="DocumentResourceId.None"/> when no context admitted it.
    /// </summary>
    public DocumentResourceId ResourceId { get; }

    /// <summary>Display width in points, or null to take it from the resource.</summary>
    public double? Width { get; }

    /// <summary>Display height in points, or null to take it from the resource.</summary>
    public double? Height { get; }

    /// <summary>Alternative text, or the empty string when the source gave none.</summary>
    public string AltText { get; }

    /// <summary>A short name for the image, used to name the part a writer emits.</summary>
    public string Name { get; }

    /// <summary>True when the source stated both dimensions rather than implying them.</summary>
    public bool HasExplicitSize => Width is not null && Height is not null;

    /// <summary>
    /// The encoded bytes, or empty when the payload is decoded samples.
    /// </summary>
    /// <remarks>
    /// Compatibility access, defined for the encoded variant only. It returns
    /// empty rather than encoding anything, so a caller that reaches for bytes on
    /// a decoded picture gets nothing instead of a re-encoding this type chose on
    /// its behalf. Anything that must tell "no bytes" from "no picture" — and
    /// every writer must — uses <see cref="TryGetEncoded"/> instead.
    /// </remarks>
    public ReadOnlyMemory<byte> Data =>
        Resource.TryGetEncoded(out ReadOnlyMemory<byte> data, out _) ? data : default;

    /// <summary>
    /// The encoded payload's media type, or the empty string for decoded samples,
    /// which have no encoding and are not given an invented one.
    /// </summary>
    /// <remarks>Compatibility access; see <see cref="Data"/>.</remarks>
    public string ContentType => Resource.MediaType ?? string.Empty;

    /// <summary>
    /// The encoded bytes and media type, when the payload has them. False for a
    /// decoded payload: nothing is encoded to satisfy the call, because the bytes
    /// would not be the document's and a lossy round trip would change the
    /// picture.
    /// </summary>
    public bool TryGetEncoded(out ReadOnlyMemory<byte> data, [NotNullWhen(true)] out string? contentType) =>
        Resource.TryGetEncoded(out data, out contentType);

    /// <summary>
    /// Resolves the size this image is drawn at, in points.
    /// </summary>
    /// <remarks>
    /// Both dimensions given are used as they are. One given preserves the
    /// intrinsic aspect ratio. Neither given converts the intrinsic pixels at
    /// <see cref="PixelsPerInch"/>. False means the answer needed an intrinsic
    /// size the resource does not have — the unplaceable case, which a caller
    /// reports rather than papering over with a default.
    /// </remarks>
    public bool TryGetDisplaySize(out double width, out double height)
    {
        if (Width is double w && Height is double h)
        {
            width = w;
            height = h;
            return true;
        }

        width = 0;
        height = 0;
        if (!Resource.HasIntrinsicSize)
            return false;

        double intrinsicWidth = Resource.PixelWidth.Value * PointsPerInch / PixelsPerInch;
        double intrinsicHeight = Resource.PixelHeight.Value * PointsPerInch / PixelsPerInch;
        if (intrinsicWidth <= 0 || intrinsicHeight <= 0)
            return false;

        switch (Width, Height)
        {
            case (double given, null):
                width = given;
                height = given * intrinsicHeight / intrinsicWidth;
                return true;
            case (null, double given):
                height = given;
                width = given * intrinsicWidth / intrinsicHeight;
                return true;
            default:
                width = intrinsicWidth;
                height = intrinsicHeight;
                return true;
        }
    }

    /// <summary>Returns the same image drawn at a different size.</summary>
    public InlineImage WithSize(double? width, double? height) =>
        new(Resource, ResourceId, width, height, AltText, Name, Presentation);

    /// <summary>Returns the same image with different alternative text.</summary>
    public InlineImage WithAltText(string? altText) =>
        new(Resource, ResourceId, Width, Height, altText, Name, Presentation);

    /// <summary>Returns the same image cropped and masked differently.</summary>
    public InlineImage WithPresentation(ImagePresentation? presentation) =>
        new(Resource, ResourceId, Width, Height, AltText, Name, presentation);

    /// <summary>
    /// Returns the same image bound to a different conversion context's entry,
    /// which is what admitting a pasted or merged picture produces.
    /// </summary>
    public InlineImage WithResourceId(DocumentResourceId resourceId) =>
        new(Resource, resourceId, Width, Height, AltText, Name, Presentation);

    private static double? Validate(double? value, string name)
    {
        if (value is null)
            return null;
        if (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                "An image dimension is a positive finite number of points, or null for auto.");
        }

        return value;
    }

    private static string Required(string contentType) =>
        string.IsNullOrWhiteSpace(contentType)
            ? throw new ArgumentException("An inline image needs a content type.", nameof(contentType))
            : contentType;
}
