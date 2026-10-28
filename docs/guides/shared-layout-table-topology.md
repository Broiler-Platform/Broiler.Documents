# Shared Layout Foundations: Table Topology Before Sizing or Border Policy

This document defines the engine-neutral 2D table topology architecture bridging rich-text document table models (`Broiler.Documents.Model` / `TableGrid`) and CSS / HTML table models (`Broiler.Layout.Engine` / `CssLayoutEngineTable`).

---

## 1. Overview & Architecture

Table layout in document processing and HTML rendering shares a core topological foundation: mapping 2D grid coordinates `(row, column)` to cell roots, handling row/column merges (`rowspan` / `colspan`), identifying continuation slots, and enumerating boundary seams between adjacent cells.

However, sizing algorithms and border arbitration policies diverge significantly:
- **Documents Engine**: Focuses on explicit column widths, page-split row breaking, office positioning, and document editing transformations (transpose, flip, insert/delete row/column).
- **Layout Engine**: Focuses on CSS auto/fixed table sizing, `visibility: collapse`, anonymous table elements, `dir="rtl"` tie-ordering, CSS `border-collapse`, and half-border clipping geometry.

To enable shared algorithmic foundations without introducing unwanted coupling or premature policy convergence, the topology engine is isolated into engine-neutral components under [`Broiler.Documents.Model.Tables`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Tables):

```
                        ┌──────────────────────────────┐
                        │   TableTopologyLimits        │
                        │   (65,536 rows, 1,024 cols,  │
                        │   2M slots, 100k slots/cell) │
                        └──────────────┬───────────────┘
                                       │
                        ┌──────────────▼───────────────┐
                        │   TableTopologyMap<TCell>    │
                        │   - Normalized occupancy     │
                        │   - Overlap resolution       │
                        │   - Edge segment enumeration │
                        └──────▲───────────────▲───────┘
                               │               │
            ┌──────────────────┴──┐         ┌──┴──────────────────┐
            │ Document Adapter    │         │ CSS / HTML Adapter  │
            ├─────────────────────┤         ├─────────────────────┤
            │ DocumentTable       │         │ HTML table rows     │
            │ TableGrid           │         │ CssSpacingBox       │
            │ Office merge model  │         │ RTL tie-breaking    │
            └─────────────────────┘         └─────────────────────┘
```

---

## 2. Core Topology Components

### 2.1 Guardrails & Limits ([`TableTopologyLimits`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Tables/TableTopologyLimits.cs))
Protects memory and execution against adversarial or pathological inputs:
- `MaxRows = 65_536`: Standard HTML / Office row upper limit.
- `MaxColumns = 1_024`: Matches HTML `MaximumColSpan` and Word grid limits.
- `MaxSlots = 2_000_000`: Maximum slots in a single table before bounding dimensions.
- `MaxSlotsPerCell = 100_000`: Maximum slots a single merged cell can cover; clamps runaway nested loops to sub-millisecond execution.
- `MaxFlatCacheSlots = 250_000`: Threshold below which flat 1D array caching is allocated; above this, O(1) sparse dictionary maps are used to prevent out-of-memory errors.

### 2.2 Topology Slot ([`TopologySlot<TCell>`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Tables/TopologySlot.cs))
An immutable value record capturing the state of an individual coordinate `(Row, Column)`:
- `IsEmpty` / `IsOccupied`: Indicates if a real cell covers this slot.
- `IsRoot`: True if `(Row, Column)` is the top-left origin anchor of the cell.
- `IsContinuation`: True if covered by a merged cell originating elsewhere.
- `RowOffset` / `ColumnOffset`: Distance from the cell's root coordinate.
- `IsTopEdge`, `IsBottomEdge`, `IsLeftEdge`, `IsRightEdge`: Geometric perimeter flags for the cell's bounding rectangle.

### 2.3 Shared Edge Segments ([`TopologyEdgeSegment<TCell>`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Tables/TopologyEdgeSegment.cs))
Represents a boundary seam between adjacent slots:
- `Orientation`: `TableEdgeOrientation.Horizontal` (between row `r-1` and `r`) or `Vertical` (between col `c-1` and `c`).
- `BoundaryIndex`: The boundary line index (0 to `RowCount` for horizontal, 0 to `ColumnCount` for vertical).
- `SlotIndex`: The perpendicular coordinate spanned by the edge segment.
- `PrimaryCell` / `NeighborCell`: The adjacent cells on either side.
- `IsOuterBoundary`: True if segment lies on the outer perimeter boundary of the table grid.
- `IsInternalSeam`: True if `PrimaryCell` and `NeighborCell` are the same merged cell instance (no interior border lines should be drawn).
- `IsSharedEdge`: True if separating two distinct real cells.
- `IsBorderingEmpty`: True if bordering an empty interior slot.
- `GetTieBreakOrder(bool isRtl)`: Returns `(FirstTieCell, SecondTieCell)` according to CSS 2.1 §17.6.2.1 tie-breaking rules.

### 2.4 Topology Map Engine ([`TableTopologyMap<TCell>`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Tables/TableTopologyMap.cs))
- Manages 2D coordinate lookups via `GetSlot(r, c)`, `GetCellAt(r, c)`, `GetMergedRoot(r, c)`, and `GetCellRange(cell)`.
- Normalizes overlaps: conflicting cells are pushed rightward to the first available slot in the row.
- Enumerates edges via `EnumerateHorizontalEdges()`, `EnumerateVerticalEdges()`, and `EnumerateAllEdges()`.

---

## 3. Engine Adapters

### 3.1 Document Table Adapter ([`DocumentTableTopologyAdapter`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Tables/DocumentTableTopologyAdapter.cs))
- `FromTable(DocumentTable)`: Extracts root cells and spans from `table.Rows`, filtering out explicit row-span continuation markers (`cell.IsRowSpanContinuation`) and constructing a normalized `TableTopologyMap<TableCell>`.
- `FromGrid(TableGrid)`: Converts an existing `TableGrid` sparse coordinate structure into `TableTopologyMap<TableCell>`.
- `TableGrid.ToTopologyMap()`: Direct extension method allowing any `TableGrid` consumer to obtain a topology map with zero boilerplate.

### 3.2 CSS / HTML Table Adapter ([`CssTableTopologyAdapter`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Tables/CssTableTopologyAdapter.cs))
- `FromRows<TCell>(rows, isRtl)`: Consumes row-major cell descriptors ([`CssCellDescriptor<TCell>`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Tables/CssTableTopologyAdapter.cs#L10)) according to the W3C HTML table processing model:
  - Tracks occupied slots across rows as rowspans project downward.
  - Automatically advances column pointers past occupied slots.
  - Transparently recognizes placeholder spacing boxes (`CssSpacingBox` / `ForSpacingBox`).
- `EnumerateCollapsibleSeams<TCell>(map, isRtl)`: Enumerates all adjacent cell seams requiring CSS border collapse:
  - Automatically omits internal seams inside merged cells.
  - Identifies `TieWinnerCell` and `TieLoserCell` according to direction:
    - Horizontal seams: top cell wins ties in both LTR and RTL.
    - Vertical seams: left cell wins ties in LTR; right cell wins ties in RTL.

---

## 4. Separation of Concerns & Engine Responsibilities

| Responsibility | Neutral Shared Layer (`Broiler.Documents.Model.Tables`) | Documents Engine (`Broiler.Documents`) | Layout Engine (`Broiler.Layout`) |
| :--- | :--- | :--- | :--- |
| **Grid Occupancy & Merge Tracking** | `TableTopologyMap<TCell>` | Implemented via `TableGrid` & adapter | Implemented via `CssTableTopologyAdapter` |
| **Adversarial Input Guardrails** | `TableTopologyLimits` (clamping dimensions & spans) | Stored and enforced | Stored and enforced |
| **Overlap Resolution** | Pushes overlapping cells rightward | Document editing validation | Standard HTML table algorithm |
| **Continuation Slots & Merged Roots** | `TopologySlot<TCell>` (`IsRoot`, `IsContinuation`, offsets) | `TableGridSlot` mapping | `CssSpacingBox` row-span tracking |
| **Edge & Seam Enumeration** | `TopologyEdgeSegment<TCell>` (`IsInternalSeam`, `IsSharedEdge`) | Internal seams suppressed in borders | Internal seams suppressed in borders |
| **Border Collapse Arbitration** | Edge tie ordering (`GetTieBreakOrder`) | Word/ODT border arbitration rules | CSS `hidden`, half-border geometry & styles |
| **Table Sizing Calculation** | *Excluded (engine-specific)* | Word grid, fixed column widths, row heights | CSS auto/fixed layout, min/max widths |
| **Document Transformations** | *Excluded (engine-specific)* | Transpose, flip, row/col insertion & deletion | *Not applicable* |
| **Pagination & Row Splitting** | *Excluded (engine-specific)* | Multi-page row breaking, header repeat | Fragment-based page slicing |

---

## 5. Verification Evidence

The shared topology architecture is verified by characterization and regression tests in [`Broiler.Documents.Model.Tests.TableTopologyMapTests`](file:///d:/Broiler.Documents/src/tests/Broiler.Documents.Model.Tests/TableTopologyMapTests.cs):

1. **`TableTopologyLimits_Clamping_WorksAsExpected`**: Verifies clamping for negative, zero, valid, and extreme spans/dimensions.
2. **`Empty_TopologyMap_HasExpectedProperties`**: Verifies empty topology maps handle out-of-bounds queries safely.
3. **`Basic_Occupancy_And_Coordinate_Lookups`**: Verifies coordinates, slot lookups, adjacent 4-cardinal neighbors, and row/column cell lists.
4. **`MergedCells_And_ContinuationSlots_Tracking`**: Verifies 2D merge tracking (0,0..1,1), verifying root vs continuation slot state, offsets, and perimeter flags.
5. **`Overlap_Resolution_PushesCellToNextAvailableSlot`**: Verifies overlapping cell placed at an occupied slot is pushed to the next free column.
6. **`Adversarial_Limits_GracefullyClamped`**: Verifies 1,000,000 x 50,000 span requests execute in sub-millisecond time and remain bounded within `MaxSlotsPerCell`.
7. **`Edge_Enumeration_Perimeter_InternalSeam_And_SharedEdge`**: Verifies perimeter boundaries, internal seams inside merges, and shared edges between distinct cells.
8. **`DocumentTableTopologyAdapter_FromTable_And_FromGrid`**: Verifies `DocumentTable` and `TableGrid` produce identical topology maps.
9. **`CssTableTopologyAdapter_FromRows_And_SpacingBoxes`**: Verifies W3C HTML table row processing and `CssSpacingBox` placeholder handling.
10. **`CrossAdapter_Equivalence_Documents_And_CSS`**: Constructs identical 3x3 tables with row/col merges through both `DocumentTableTopologyAdapter` and `CssTableTopologyAdapter`. Proves exact agreement across all 9 slots (occupancy, root row/col, spans, continuation, perimeter flags) and all horizontal/vertical edge segments.
11. **`CssTableTopologyAdapter_RTL_TieBreak_Arbitration`**: Verifies vertical seam tie-breaking favors left cell in LTR and right cell in RTL per CSS 2.1 §17.6.2.1.
