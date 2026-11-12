using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Odt;

/// <summary>
/// The outline of a custom shape (<c>draw:custom-shape</c>) that its
/// <c>draw:enhanced-geometry</c> draws with straight lines: a star, an arrow, a
/// cube. Each closed sub-path is a polygon, its corners in fractions of the
/// shape's box.
/// </summary>
/// <remarks>
/// <para>
/// The path (<c>draw:enhanced-path</c>, ODF 1.3 §19.145) is in the units of
/// <c>svg:viewBox</c>. A coordinate is a number, a modifier (<c>$0</c>, from
/// <c>draw:modifiers</c>) or an equation (<c>?f3</c>, a
/// <c>draw:equation</c>'s <c>draw:formula</c>), and an equation can use the
/// others, the arithmetic operators, <c>abs</c>, <c>sqrt</c>, <c>sin</c>,
/// <c>cos</c>, <c>tan</c>, <c>atan</c>, <c>atan2</c>, <c>min</c>, <c>max</c>,
/// <c>if</c> and the box's <c>left</c>, <c>top</c>, <c>right</c>,
/// <c>bottom</c>, <c>width</c> and <c>height</c>.
/// </para>
/// <para>
/// Only <c>M</c>, <c>L</c>, <c>Z</c> and <c>N</c> are read. A path with a curve,
/// an arc or a sub-path drawn without fill or line is not, and its shape keeps
/// the box it was drawn as before. <c>draw:mirror-horizontal</c> and
/// <c>draw:mirror-vertical</c> flip the outline in its box.
/// </para>
/// </remarks>
internal static class OdtCustomShapePath
{
    /// <summary>The most sub-paths, and corners in one, read from a path.</summary>
    private const int MaxPaths = 64, MaxPoints = 512;

    /// <summary>The closed polygons <paramref name="geometry"/> draws, or null when its path is not straight lines.</summary>
    /// <param name="geometry">The shape's <c>draw:enhanced-geometry</c>.</param>
    /// <param name="width">The shape's width, in points.</param>
    /// <param name="height">The shape's height, in points.</param>
    /// <param name="rotation">
    /// The angle the shape is drawn turned by, as <see cref="Model.DocumentShape.RotationRadians"/>
    /// holds it. Writer mirrors a turned shape on the page, not in its own box:
    /// flip_and_rotate.odt's bent arrow, mirrored top to bottom and turned by
    /// 135°, opens upwards. Mirrored in its box and then turned, it opened to
    /// the right. Turning the mirrored outline back by twice the angle about
    /// the box's centre gives the page's mirror once the shape is turned.
    /// </param>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>>? Read(XElement? geometry, double width = 1, double height = 1, double rotation = 0)
    {
        if (geometry is null ||
            (string?)geometry.Attribute(OdtNamespaces.Draw + "enhanced-path") is not { Length: > 0 } path ||
            !TryReadViewBox((string?)geometry.Attribute(OdtNamespaces.Svg + "viewBox"), out double left, out double top, out double boxWidth, out double boxHeight))
            return null;

        var evaluator = new Evaluator(geometry, left, top, boxWidth, boxHeight);
        var paths = new List<IReadOnlyList<(double X, double Y)>>();
        List<(double X, double Y)>? current = null;
        bool flipX = (string?)geometry.Attribute(OdtNamespaces.Draw + "mirror-horizontal") == "true";
        bool flipY = (string?)geometry.Attribute(OdtNamespaces.Draw + "mirror-vertical") == "true";

        List<string> tokens = Tokenize(path);
        char command = '\0';
        int i = 0;
        while (i < tokens.Count)
        {
            string token = tokens[i];
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                command = token[0];
                i++;
                switch (command)
                {
                    case 'M':
                        Finish();
                        current = [];
                        break;
                    case 'L':
                        if (current is null)
                            return null;
                        break;
                    case 'Z':
                    case 'N':
                        Finish();
                        command = '\0';
                        break;
                    default:
                        return null;
                }
                continue;
            }

            if (command is not ('M' or 'L') || current is null || i + 1 >= tokens.Count ||
                !evaluator.TryParameter(tokens[i], out double x) || !evaluator.TryParameter(tokens[i + 1], out double y))
                return null;
            i += 2;
            double u = (x - left) / boxWidth, v = (y - top) / boxHeight;
            if (!double.IsFinite(u) || !double.IsFinite(v) || current.Count >= MaxPoints)
                return null;
            current.Add((flipX ? 1 - u : u, flipY ? 1 - v : v));
            // Pairs after a moveto are linetos.
            if (command == 'M')
                command = 'L';
        }

        Finish();
        if (paths.Count == 0)
            return null;
        if (flipX != flipY && rotation != 0 && double.IsFinite(rotation) && width > 0 && height > 0)
        {
            double turn = -2 * rotation, cos = Math.Cos(turn), sin = Math.Sin(turn);
            paths = [.. paths.Select(polygon => (IReadOnlyList<(double X, double Y)>)[.. polygon.Select(point =>
            {
                double px = (point.X - 0.5) * width, py = (point.Y - 0.5) * height;
                return ((px * cos - py * sin) / width + 0.5, (px * sin + py * cos) / height + 0.5);
            })])];
        }
        return paths;

        void Finish()
        {
            if (current is { Count: >= 3 } && paths.Count < MaxPaths)
                paths.Add(current);
            current = null;
        }
    }

    private static bool TryReadViewBox(string? value, out double left, out double top, out double width, out double height)
    {
        left = top = 0;
        width = height = 21600;
        if (value is null)
            return true;
        string[] parts = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out height) &&
            width > 0 && height > 0;
    }

    private static List<string> Tokenize(string path)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < path.Length)
        {
            char c = path[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
                continue;
            }
            if (char.IsLetter(c))
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }
            int start = i;
            i++;
            while (i < path.Length && !char.IsWhiteSpace(path[i]) && path[i] != ',' &&
                !(char.IsLetter(path[i]) && path[start] != '?' && path[i] is not ('e' or 'E')))
                i++;
            tokens.Add(path[start..i]);
        }
        return tokens;
    }

    /// <summary>The values of a geometry's modifiers and equations.</summary>
    private sealed class Evaluator
    {
        private const int MaxDepth = 64;
        private readonly double[] _modifiers;
        private readonly string[] _formulas;
        private readonly double?[] _values;
        private readonly bool[] _evaluating;
        private readonly double _left, _top, _width, _height;

        public Evaluator(XElement geometry, double left, double top, double width, double height)
        {
            (_left, _top, _width, _height) = (left, top, width, height);
            _modifiers = ((string?)geometry.Attribute(OdtNamespaces.Draw + "modifiers") ?? string.Empty)
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.NaN)
                .ToArray();
            _formulas = geometry.Elements(OdtNamespaces.Draw + "equation")
                .Select(equation => (string?)equation.Attribute(OdtNamespaces.Draw + "formula") ?? string.Empty)
                .ToArray();
            _values = new double?[_formulas.Length];
            _evaluating = new bool[_formulas.Length];
        }

        public bool TryParameter(string token, out double value)
        {
            value = Parameter(token, 0);
            return double.IsFinite(value);
        }

        private double Parameter(string token, int depth)
        {
            if (token.StartsWith('?'))
                return token.Length > 2 && token[1] == 'f' && int.TryParse(token[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    ? Equation(index, depth + 1)
                    : double.NaN;
            if (token.StartsWith('$'))
                return int.TryParse(token[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int modifier) && modifier >= 0 && modifier < _modifiers.Length
                    ? _modifiers[modifier]
                    : double.NaN;
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : Constant(token);
        }

        private double Equation(int index, int depth)
        {
            if (index < 0 || index >= _formulas.Length || depth > MaxDepth || _evaluating[index])
                return double.NaN;
            if (_values[index] is double known)
                return known;
            _evaluating[index] = true;
            double value = new Parser(_formulas[index], this, depth).Parse();
            _evaluating[index] = false;
            _values[index] = value;
            return value;
        }

        private double Constant(string name) => name switch
        {
            "pi" => Math.PI,
            "left" => _left,
            "top" => _top,
            "right" => _left + _width,
            "bottom" => _top + _height,
            "width" or "logwidth" => _width,
            "height" or "logheight" => _height,
            "xstretch" or "ystretch" or "hasstroke" or "hasfill" => 1,
            _ => double.NaN,
        };

        /// <summary>A recursive-descent reading of one <c>draw:formula</c>.</summary>
        private sealed class Parser(string text, Evaluator owner, int depth)
        {
            private int _position;

            public double Parse()
            {
                double value = Sum();
                SkipSpace();
                return _position == text.Length ? value : double.NaN;
            }

            private double Sum()
            {
                double value = Product();
                while (true)
                {
                    SkipSpace();
                    if (Take('+')) value += Product();
                    else if (Take('-')) value -= Product();
                    else return value;
                }
            }

            private double Product()
            {
                double value = Unary();
                while (true)
                {
                    SkipSpace();
                    if (Take('*')) value *= Unary();
                    else if (Take('/')) value /= Unary();
                    else return value;
                }
            }

            private double Unary()
            {
                SkipSpace();
                if (Take('-')) return -Unary();
                if (Take('+')) return Unary();
                return Primary();
            }

            private double Primary()
            {
                SkipSpace();
                if (Take('('))
                {
                    double inner = Sum();
                    SkipSpace();
                    return Take(')') ? inner : double.NaN;
                }

                int start = _position;
                if (_position < text.Length && (text[_position] == '?' || text[_position] == '$'))
                    _position++;
                while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] == '.' ||
                    ((text[_position] is '+' or '-') && _position > start && text[_position - 1] is 'e' or 'E' && char.IsDigit(text[start]))))
                    _position++;
                string word = text[start.._position];
                if (word.Length == 0)
                    return double.NaN;

                SkipSpace();
                if (char.IsLetter(word[0]) && Take('('))
                {
                    var arguments = new List<double> { Sum() };
                    SkipSpace();
                    while (Take(','))
                    {
                        arguments.Add(Sum());
                        SkipSpace();
                    }
                    return Take(')') ? Call(word, arguments) : double.NaN;
                }

                return owner.Parameter(word, depth);
            }

            private static double Call(string name, List<double> a) => (name, a.Count) switch
            {
                ("abs", 1) => Math.Abs(a[0]),
                ("sqrt", 1) => Math.Sqrt(a[0]),
                ("sin", 1) => Math.Sin(a[0]),
                ("cos", 1) => Math.Cos(a[0]),
                ("tan", 1) => Math.Tan(a[0]),
                ("atan", 1) => Math.Atan(a[0]),
                ("atan2", 2) => Math.Atan2(a[0], a[1]),
                ("min", 2) => Math.Min(a[0], a[1]),
                ("max", 2) => Math.Max(a[0], a[1]),
                ("if", 3) => a[0] > 0 ? a[1] : a[2],
                _ => double.NaN,
            };

            private void SkipSpace()
            {
                while (_position < text.Length && char.IsWhiteSpace(text[_position]))
                    _position++;
            }

            private bool Take(char c)
            {
                if (_position < text.Length && text[_position] == c)
                {
                    _position++;
                    return true;
                }
                return false;
            }
        }
    }
}
