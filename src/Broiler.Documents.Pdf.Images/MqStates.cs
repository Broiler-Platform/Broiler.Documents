namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// The MQ coder's probability estimation table, ITU-T T.88 Annex E.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a transcribed normative table — the third in this
/// repository, after the fax code tables and the CFF standard strings.</strong>
/// Forty-seven states of LPS probability, next-state-on-MPS,
/// next-state-on-LPS, and a conditional exchange flag. They are tuned constants
/// the standard's designers chose; nothing derives them, and an implementation
/// either reproduces them or decodes nothing at all.
/// </para>
/// <para>
/// SRC-019 declined to approve this in advance — "normative constants with no
/// authored alternative", deferred to SRC-017's open question — and the register
/// records the table as taken ahead of that decision rather than under it. No
/// JBIG2-derived capability may be claimed as supported until the row closes.
/// </para>
/// <para>
/// It lives in a file of its own, one state per line, for two reasons. A
/// reviewer holding T.88 can check it against Table E.1 by eye, which is the
/// only check available — the standard's test sequence is official test material
/// and the source rules exclude it. And the decoder beside it is then a
/// procedure with no data in it, so the part an engineer can be held to and the
/// part a reviewer has to weigh are not tangled together.
/// </para>
/// <para>
/// The test-suite encoder shares this table deliberately. A second transcription
/// would be a second chance to make the same mistake while looking like
/// corroboration.
/// </para>
/// </remarks>
internal static class MqStates
{
    /// <summary>Qe, NMPS, NLPS, SWITCH — one state per line, in index order.</summary>
    public static readonly (ushort Qe, byte Nmps, byte Nlps, byte Switch)[] All =
    [
        (0x5601, 1, 1, 1), (0x3401, 2, 6, 0), (0x1801, 3, 9, 0), (0x0AC1, 4, 12, 0),
        (0x0521, 5, 29, 0), (0x0221, 38, 33, 0), (0x5601, 7, 6, 1), (0x5401, 8, 14, 0),
        (0x4801, 9, 14, 0), (0x3801, 10, 14, 0), (0x3001, 11, 17, 0), (0x2401, 12, 18, 0),
        (0x1C01, 13, 20, 0), (0x1601, 29, 21, 0), (0x5601, 15, 14, 1), (0x5401, 16, 14, 0),
        (0x5101, 17, 15, 0), (0x4801, 18, 16, 0), (0x3801, 19, 17, 0), (0x3401, 20, 18, 0),
        (0x3001, 21, 19, 0), (0x2801, 22, 19, 0), (0x2401, 23, 20, 0), (0x2201, 24, 21, 0),
        (0x1C01, 25, 22, 0), (0x1801, 26, 23, 0), (0x1601, 27, 24, 0), (0x1401, 28, 25, 0),
        (0x1201, 29, 26, 0), (0x1101, 30, 27, 0), (0x0AC1, 31, 28, 0), (0x09C1, 32, 29, 0),
        (0x08A1, 33, 30, 0), (0x0521, 34, 31, 0), (0x0441, 35, 32, 0), (0x02A1, 36, 33, 0),
        (0x0221, 37, 34, 0), (0x0141, 38, 35, 0), (0x0111, 39, 36, 0), (0x0085, 40, 37, 0),
        (0x0049, 41, 38, 0), (0x0025, 42, 39, 0), (0x0015, 43, 40, 0), (0x0009, 44, 41, 0),
        (0x0005, 45, 42, 0), (0x0001, 45, 43, 0), (0x5601, 46, 46, 0),
    ];
}
