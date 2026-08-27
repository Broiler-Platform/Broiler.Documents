using System;
using System.Globalization;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// The 2-by-3 affine transform PDF uses for <c>cm</c>, <c>Tm</c>, and Form
/// XObject placement (clause 8.3.3), stored in the format's own <c>[a b c d e f]</c>
/// order.
/// </summary>
internal readonly struct PdfMatrix : IEquatable<PdfMatrix>
{
    public PdfMatrix(double a, double b, double c, double d, double e, double f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    public static PdfMatrix Identity => new(1, 0, 0, 1, 0, 0);

    public double A { get; }

    public double B { get; }

    public double C { get; }

    public double D { get; }

    public double E { get; }

    public double F { get; }

    public static PdfMatrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    /// <summary>This matrix applied first, then <paramref name="other"/>.</summary>
    public PdfMatrix Concat(PdfMatrix other) => new(
        (A * other.A) + (B * other.C),
        (A * other.B) + (B * other.D),
        (C * other.A) + (D * other.C),
        (C * other.B) + (D * other.D),
        (E * other.A) + (F * other.C) + other.E,
        (E * other.B) + (F * other.D) + other.F);

    public (double X, double Y) Transform(double x, double y) =>
        ((A * x) + (C * y) + E, (B * x) + (D * y) + F);

    /// <summary>
    /// The vertical scale this matrix applies, used to turn a text-space font
    /// size into a device-space one. Taking the length of the transformed unit
    /// y-vector keeps rotated and skewed text sensible instead of reading the
    /// <c>d</c> component alone.
    /// </summary>
    public double VerticalScale => Math.Sqrt((C * C) + (D * D));

    /// <summary>The horizontal scale, by the same argument as <see cref="VerticalScale"/>.</summary>
    public double HorizontalScale => Math.Sqrt((A * A) + (B * B));

    /// <summary>True when every component is finite, which every use here requires.</summary>
    public bool IsFinite =>
        double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C) &&
        double.IsFinite(D) && double.IsFinite(E) && double.IsFinite(F);

    public bool Equals(PdfMatrix other) =>
        A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C) &&
        D.Equals(other.D) && E.Equals(other.E) && F.Equals(other.F);

    public override bool Equals(object? obj) => obj is PdfMatrix other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(A, B, C, D, E, F);

    public static bool operator ==(PdfMatrix left, PdfMatrix right) => left.Equals(right);

    public static bool operator !=(PdfMatrix left, PdfMatrix right) => !left.Equals(right);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{A} {B} {C} {D} {E} {F}]");
}
