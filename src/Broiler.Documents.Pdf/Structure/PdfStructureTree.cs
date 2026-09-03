using System.Collections.Generic;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Structure;

/// <summary>
/// The document's structure tree, read for one thing only: the order its content
/// is meant to be read in.
/// </summary>
/// <remarks>
/// <para>
/// A PDF says where glyphs are drawn, not what order they are read in, and the
/// geometric pass that infers it is a documented heuristic that a two-column
/// page, a sidebar, or a table can defeat. A tagged document already carries the
/// answer: its structure tree is a sequence of elements, and each names the
/// marked content on the page that belongs to it. Walking that sequence gives
/// the order the author declared.
/// </para>
/// <para>
/// <strong>Only the order is taken.</strong> Every element's role — <c>/P</c>,
/// <c>/H1</c>, <c>/L</c>, <c>/Table</c> — is ignored, and so is the role map.
/// Consuming roles would mean claiming to reproduce a document's logical
/// structure, which is the separate accessibility architecture PDF roadmap §14.2
/// scopes and which carries conformance and assistive-technology obligations
/// this release does not meet. Reading the sequence carries none of them: it
/// replaces a guess about order with a statement about order, and nothing else.
/// </para>
/// <para>
/// Nothing here is trusted blindly. A tree that does not account for every
/// fragment on a page is not used for that page, because a partial order is
/// worse than an honest heuristic — it would silently drop the untagged half of
/// the content or append it somewhere arbitrary.
/// </para>
/// </remarks>
internal sealed class PdfStructureTree
{
    /// <summary>How deep the element hierarchy is followed before it is abandoned.</summary>
    private const int MaxDepth = 64;

    /// <summary>How many nodes are visited before the walk stops.</summary>
    private const int MaxNodes = 200_000;

    private readonly Dictionary<(int Page, int Mcid), int> _order;

    private PdfStructureTree(Dictionary<(int Page, int Mcid), int> order, bool truncated)
    {
        _order = order;
        IsTruncated = truncated;
    }

    /// <summary>How many marked-content leaves the walk placed in order.</summary>
    public int LeafCount => _order.Count;

    /// <summary>
    /// True when a depth or node bound stopped the walk, so the order is
    /// incomplete and must not be relied on.
    /// </summary>
    public bool IsTruncated { get; }

    /// <summary>
    /// Reads the catalog's structure tree, or null where the document carries
    /// none or it yields no usable order.
    /// </summary>
    public static PdfStructureTree? Read(
        PdfObjectStore store,
        PdfDictionary catalog,
        IReadOnlyList<PdfPage> pages)
    {
        if (store.Resolve(catalog["StructTreeRoot"]) is not PdfDictionary root)
            return null;

        // The page a structure element names is an indirect reference to the page
        // dictionary, and the store caches by object number, so the resolved
        // instance identifies the page.
        var index = new Dictionary<PdfDictionary, int>();
        for (int i = 0; i < pages.Count; i++)
            index[pages[i].Dictionary] = i;

        var walk = new Walk(store, index);
        walk.Visit(root["K"], page: null, depth: 0);

        return walk.Order.Count > 0 ? new PdfStructureTree(walk.Order, walk.Truncated) : null;
    }

    /// <summary>
    /// The position of one page's marked-content item in the declared order, or
    /// -1 where the tree does not place it.
    /// </summary>
    public int OrderOf(int page, int mcid) =>
        mcid >= 0 && _order.TryGetValue((page, mcid), out int order) ? order : -1;

    /// <summary>
    /// Whether every fragment on this page is placed by the tree. A page the tree
    /// only partly accounts for falls back to geometry whole, because mixing a
    /// declared order with an inferred one produces a sequence neither the
    /// document nor the heuristic asked for.
    /// </summary>
    public bool Covers(int page, IReadOnlyList<Text.PdfTextFragment> fragments)
    {
        if (IsTruncated || fragments.Count == 0)
            return false;

        foreach (Text.PdfTextFragment fragment in fragments)
        {
            if (OrderOf(page, fragment.Mcid) < 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The depth-first walk. Held as a type rather than a closure so the cycle
    /// set, the node budget, and the inherited page travel together.
    /// </summary>
    private sealed class Walk
    {
        private readonly PdfObjectStore _store;
        private readonly Dictionary<PdfDictionary, int> _pages;
        private readonly HashSet<PdfDictionary> _visited = [];
        private int _nodes;

        public Walk(PdfObjectStore store, Dictionary<PdfDictionary, int> pages)
        {
            _store = store;
            _pages = pages;
        }

        public Dictionary<(int Page, int Mcid), int> Order { get; } = [];

        public bool Truncated { get; private set; }

        public void Visit(PdfObject? node, PdfDictionary? page, int depth)
        {
            if (depth > MaxDepth || ++_nodes > MaxNodes)
            {
                Truncated = true;
                return;
            }

            switch (_store.Resolve(node))
            {
                // A bare integer is a marked-content id on whichever page the
                // enclosing element named.
                case PdfNumber number:
                    Record(page, number.ToInt32());
                    break;

                case PdfArray array:
                    foreach (PdfObject item in array)
                        Visit(item, page, depth + 1);
                    break;

                case PdfDictionary dictionary:
                    VisitDictionary(dictionary, page, depth);
                    break;
            }
        }

        private void VisitDictionary(PdfDictionary dictionary, PdfDictionary? page, int depth)
        {
            // A structure tree is a tree, but a malformed one can point back up
            // it. Elements are visited once; a marked-content reference is a leaf
            // and cannot recur.
            string type = (_store.Resolve(dictionary["Type"]) as PdfName)?.Value ?? string.Empty;
            PdfDictionary? owner = _store.Resolve(dictionary["Pg"]) as PdfDictionary ?? page;

            switch (type)
            {
                case "MCR":
                    Record(owner, (_store.Resolve(dictionary["MCID"]) as PdfNumber)?.ToInt32() ?? -1);
                    return;

                // A reference to an object rather than to marked content: an
                // annotation or an XObject placed in the flow. It carries no
                // marked content of its own, and this pass is only ordering text.
                case "OBJR":
                    return;
            }

            if (!_visited.Add(dictionary))
            {
                Truncated = true;
                return;
            }

            Visit(dictionary["K"], owner, depth + 1);
        }

        private void Record(PdfDictionary? page, int mcid)
        {
            if (mcid < 0 || page is null || !_pages.TryGetValue(page, out int number))
                return;

            // First placement wins. A document that names one piece of content
            // twice has said two different things about where it is read, and the
            // earlier statement is the one the walk has already ordered around.
            (int, int) key = (number, mcid);
            if (!Order.ContainsKey(key))
                Order[key] = Order.Count;
        }
    }
}
