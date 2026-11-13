using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Rtf;

internal sealed class RtfWriteLists
{
    private readonly Action _reportApproximation;
    private readonly List<ListDefinition> _instances = [];
    private readonly Dictionary<int, (int Instance, int Level)> _paragraphs = [];

    public RtfWriteLists(RichTextDocument document, Action reportApproximation)
    {
        _reportApproximation = reportApproximation;
        _paragraphs = RegisterFlow(document.Paragraphs);
    }

    public Dictionary<int, (int Instance, int Level)> RegisterFlow(IReadOnlyList<RichTextParagraph> paragraphs)
    {
        var active = new Dictionary<ListDefinition, int>();
        var references = new Dictionary<int, (int Instance, int Level)>();
        for (int index = 0; index < paragraphs.Count; index++)
        {
            ParagraphStyle style = paragraphs[index].Style;
            ListReference? reference = style.List;
            if (reference is null && style.ListKind != ListKind.None)
                reference = new ListReference(
                    style.ListKind == ListKind.Bullet ? ListDefinition.PlainBullets : ListDefinition.PlainNumbers,
                    Math.Clamp(style.IndentLevel - 1, 0, ListDefinition.MaxLevels - 1));
            if (reference is not { } item)
            {
                active.Remove(ListDefinition.PlainBullets);
                active.Remove(ListDefinition.PlainNumbers);
                continue;
            }

            int level = Math.Clamp(item.Level, 0, ListDefinition.MaxLevels - 1);
            ListDefinition definition = item.Definition;
            if (!active.TryGetValue(definition, out int id) || item.RestartAt.HasValue)
            {
                ListDefinition written = item.RestartAt is int start
                    ? definition.WithLevel(level, definition.LevelAt(level) with { Start = start })
                    : definition;
                if ((item.RestartAt.HasValue && level > 0) || definition.CounterGroup != definition)
                    _reportApproximation();

                id = _instances.Count + 1;
                _instances.Add(written);
                active[definition] = id;
            }

            if (!HasNativeMarker(definition.LevelAt(level)))
                _reportApproximation();
            references[index] = (id, level);
        }
        return references;
    }

    private static bool HasNativeMarker(ListLevel level) => level.Format switch
    {
        ListNumberFormat.None => true,
        ListNumberFormat.Bullet => IsSingleBullet(level.Template),
        ListNumberFormat.Decimal or ListNumberFormat.UpperRoman or ListNumberFormat.LowerRoman or
            ListNumberFormat.UpperLetter or ListNumberFormat.LowerLetter =>
            EncodeNumberedTemplate(level.Template) is not null,
        _ => false,
    };

    private static (int Length, string Text, string Positions)? EncodeNumberedTemplate(string template)
    {
        var text = new StringBuilder();
        var positions = new StringBuilder();
        int length = 0;
        for (int index = 0; index < template.Length; index++)
        {
            char character = template[index];
            if (++length > 255)
                return null;
            if (character == '%' && index + 1 < template.Length && template[index + 1] is >= '1' and <= '9')
            {
                int level = template[++index] - '1';
                text.Append("\\'").Append(level.ToString("x2", CultureInfo.InvariantCulture));
                positions.Append("\\'").Append(length.ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (character is >= ' ' and <= '~' and not ('{' or '}' or '\\' or ';' or '%'))
                text.Append(character);
            else
                return null;
        }
        return positions.Length == 0 ? null : (length, text.ToString(), positions.ToString());
    }

    private static bool IsSingleBullet(string marker) =>
        marker.Length == 1 && !char.IsControl(marker[0]) && !char.IsSurrogate(marker[0]);

    private readonly Dictionary<string, int> _characterStyles = new(StringComparer.OrdinalIgnoreCase);

    public int CharacterStyle(string name)
    {
        if (!_characterStyles.TryGetValue(name, out int id))
        {
            id = _characterStyles.Count + 1;
            _characterStyles.Add(name, id);
        }
        return id;
    }

    public void WriteCharacterStyles(StringBuilder output, Action<StringBuilder, string> escape)
    {
        if (_characterStyles.Count == 0)
            return;
        output.Append("{\\stylesheet");
        foreach (var (name, id) in _characterStyles)
        {
            output.Append("{\\*\\cs").Append(id).Append("\\additive ");
            escape(output, name);
            output.Append(";}");
        }
        output.Append('}');
    }

    public void WriteTables(StringBuilder output)
    {
        if (_instances.Count == 0)
            return;

        output.Append("{\\*\\listtable");
        for (int i = 0; i < _instances.Count; i++)
        {
            output.Append("{\\list\\listtemplateid").Append(i + 1);
            for (int level = 0; level < ListDefinition.MaxLevels; level++)
                WriteLevel(output, _instances[i].LevelAt(level), level);
            output.Append("\\listid").Append(i + 1).Append('}');
        }

        output.Append("}{\\*\\listoverridetable");
        for (int i = 1; i <= _instances.Count; i++)
            output.Append("{\\listoverride\\listid").Append(i).Append("\\listoverridecount0\\ls").Append(i).Append('}');
        output.Append('}');
    }

    public void WriteReference(StringBuilder output, int index)
    {
        WriteReference(output, _paragraphs, index);
    }

    public static void WriteReference(StringBuilder output, Dictionary<int, (int Instance, int Level)> references, int index)
    {
        if (references.TryGetValue(index, out var item))
            output.Append("\\ls").Append(item.Instance).Append("\\ilvl").Append(item.Level);
    }

    private static void WriteLevel(StringBuilder output, ListLevel style, int level)
    {
        int format = style.Format switch
        {
            ListNumberFormat.UpperRoman => 1,
            ListNumberFormat.LowerRoman => 2,
            ListNumberFormat.UpperLetter => 3,
            ListNumberFormat.LowerLetter => 4,
            ListNumberFormat.Bullet => 23,
            ListNumberFormat.None => 255,
            _ => 0,
        };

        output.Append("{\\listlevel\\levelnfc").Append(format).Append("\\leveljc0\\levelfollow0\\levelstartat")
            .Append(style.Start).Append("{\\leveltext ");
        if (format == 23)
        {
            short bullet = unchecked((short)(IsSingleBullet(style.Template)
                ? style.Template[0] : ListDefinition.PlainBullets.LevelAt(level).Template[0]));
            output.Append("\\'01\\u").Append(bullet).Append("?;}{\\levelnumbers;}");
        }
        else if (format == 255)
            output.Append("\\'00;}{\\levelnumbers;}");
        else
        {
            var marker = EncodeNumberedTemplate(style.Template) ??
                EncodeNumberedTemplate(ListLevel.DecimalAt(level).Template)!.Value;
            output.Append("\\'").Append(marker.Length.ToString("x2", CultureInfo.InvariantCulture))
                .Append(marker.Text).Append(";}{\\levelnumbers").Append(marker.Positions).Append(";}");
        }
        output.Append('}');
    }
}
