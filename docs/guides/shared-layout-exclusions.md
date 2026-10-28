# Shared Layout Foundations: Rectangle Exclusions and Available Line Bands

This document characterizes the layout exclusions model bridging `Broiler.Layout`'s CSS float bands (`LineFloatBands`) and `Broiler.Documents.Model`'s rich-text wrapping exclusions (`TextWrapExclusions`), defines the unit-neutral geometry kernel (`ExclusionBandQueryEngine`), and documents the policy decisions for moving CSS boxes, multi-obstacle resolution, clearance descent, and degenerate inputs.

---

## 1. Characterization: `LineFloatBands` vs `TextWrapExclusions`

| Dimension | `LineFloatBands` (CSS2.1 §9.5, §9.4.2) | `TextWrapExclusions` (Word Processing / Documents) | Unit-Neutral Kernel (`ExclusionBandQueryEngine`) |
|---|---|---|---|
| **Coordinate Space & Units** | CSS pixels (`double`), relative to block formatting context. | Points or zoomed canvas units (`double`), relative to text column. | Unit-neutral Cartesian coordinates (`double`). |
| **Obstacle Positioning** | Floats are explicitly placed either `float: left` or `float: right`. Margin boxes narrow the content area from outer edges inward. | Shapes/tables positioned at arbitrary $(X, Y)$ inside or straddling the column. Modes: `Square`, `TopAndBottom`, `None`. | Supports both explicit left/right floats and arbitrary interior rectangles with side-selection policies. |
| **Band Topology** | Single continuous interval `[LeftAt, RightAt]`. Left and right floats narrow the line box simultaneously. | Single interval. A central obstacle divides the column; `WrapSide.Largest` picks the wider side, `Left` picks left, `Right` picks right. | Supports both single-band resolution (CSS float edges and Document largest-side selection) and multi-band decomposition (`BothSides` disjoint intervals). |
| **Zero-Height Queries** | Hairline rule: `Math.Max(height, 0.01)`. A zero-height line occupies its top Y and must still detect overlapping floats. | Historical `Intersects(top, bottom)` required `Top < bottom && Bottom > top`, which missed obstacles at line boundaries when `height == 0`. | Unified hairline evaluation: queries with $height \le 0$ evaluate as a slice of thickness `HairlineTolerance` (default `0.01`). |
| **Touching Edges & Tolerances** | Strict inequality: `floatTop < top + height && floatBottom > top`. Touching edges do not overlap, but raw floating-point arithmetic could jitter. | Strict inequality: `Top < bottom && Bottom > top`. | Parameterized epsilon tolerance ($10^{-4}$ default). Touching edges ($bottom == Top$ or $top == Bottom$) do not collide. |
| **Clearance & Descent Step** | `NextBandBottom(top, height)`: Finds the **shallowest** bottom edge ($\min(bottom)$) among overlapping floats ($bottom > top$). Line drops incrementally as floats clear. | `PushBelow(top, bottom)`: Finds the **deepest** bottom edge ($\max(bottom)$) among overlapping obstacles. Line clears all overlapping obstacles in one step. | Supports both modes via explicit methods: `ShallowestLowerEdge` (CSS step) and `DeepestLowerEdge` (Word step). |
| **Multi-Obstacle Ordering** | Order-independent for `LeftAt` and `RightAt` because $\max$ and $\min$ over all floats commute. | Sequential iteration over obstacles. Intermediate room calculations determine side choice. | Supports both `SequentialClamping` (exact legacy document compatibility) and `GlobalIntervals` (interval subtraction with deterministic tie-breaking). |
| **Bounded Retry & Fallback** | `CssLayoutEngine.DropLineBelowNarrowBands`: Drops line up to 64 steps. Once all floats clear, line overflows below. | `TextWrapExclusions.Resolve`: Loops up to 8 attempts. If still blocked, line takes full column width at current $Y$ and draws through. | Parameterized `ResolveLine` with configurable `maxAttempts` (8 for Word, 64 for CSS) and selectable `BlockedBandFallback`. |

---

## 2. Policy Decisions

### 2.1 Snapshots vs Explicit Updates for Moving CSS Boxes

During CSS inline formatting, floats declared within inline content are placed incrementally as lines are formed. Moving inline content can shift an already placed float box downwards (`InlineFloats.AnyMoved`).

**Decision**:
- The unit-neutral kernel supports both:
  1. **Snapshots** (`CreateSnapshot()`): Produces an isolated, immutable deep copy of obstacle geometry. Layout passes can freeze obstacle state for lookahead line wrapping without risk of concurrent mutation.
  2. **Direct Rebuild / Registration**: Because float counts within a single block formatting context are small (typically $< 10$, rarely $> 50$), recreating or updating an adapter instance takes sub-microsecond time.
- `CssFloatBandAdapter` provides explicit `CreateSnapshot()` to isolate line-fitting passes from subsequent float repositioning.

### 2.2 Multiple Central Obstacles & Insertion Order Effects

When two or more floating shapes occupy the same vertical slice in a document:
- Under **`SequentialClamping`** (the default for document compatibility), obstacles are processed in insertion sequence. Obstacle 1 narrows the available band from the left or right; obstacle 2 evaluates remaining room against the already narrowed band.
  - Tie-breaking: when room on the left equals room on the right ($roomLeft \ge roomRight$), text keeps to the left (`keepLeft = true`), maintaining historical Word/Writer parity.
- Under **`GlobalIntervals`**, obstacles partition the container into disjoint free horizontal spans $[L_1, R_1], [L_2, R_2], \dots$.
  - `Largest`: chooses the interval with maximal width; on tie, chooses the leftmost interval.
  - `BothSides`: returns all disjoint free intervals whose width meets or exceeds `minUsableWidth`.

### 2.3 Fully Blocked Bands & Fallback

When a line cannot fit at the current Y (due to full-width clearance obstacles or overlapping shapes leaving less than `minUsableWidth` room):
- The engine descends using the chosen step strategy (`Deepest` or `Shallowest`).
- If `maxAttempts` is reached without finding space, the engine invokes `BlockedBandFallback`:
  - `FullColumnAtCurrentY`: The line draws through with full container width at the current descended Y. This prevents pathological infinite loops or document truncation while ensuring all text remains visible.
  - `FullColumnAtInitialY`: Line resets to start position.
  - `ZeroWidth`: Returns zero width, allowing paginators to break to a new page or column.

### 2.4 Degenerate and Invalid Inputs

- **Non-finite coordinates**: Obstacles with `NaN` or `±Infinity` coordinates are rejected upon insertion (`Bounds.IsValid == false`) and ignored, preventing canvas corruption.
- **Inverted boundaries**: Rectangles where `Right < Left` or `Bottom < Top` are treated as invalid and empty.
- **Zero/negative container width**: Returns a zero-width band `LineBandInterval(containerLeft, 0.0)` immediately.
- **Zero/negative line height**: Handled via `HairlineTolerance` ($0.01$), ensuring lines before metric measurement still intersect obstacles.

---

## 3. Architecture & Adapters

```
+-------------------------------------------------------+
|             Unit-Neutral Geometry Kernel              |
|              (ExclusionBandQueryEngine)               |
|                                                       |
|  - ExclusionRect (LTRB, XYWH, Margins, Intersections) |
|  - ExclusionObstacle (Bounds, WrapKind, SidePolicy)   |
|  - LineBandInterval (Left, Width, Right)              |
|  - Tolerances & Hairlines (Epsilon, Zero-Height)      |
|  - Clearance Calculation (Shallowest, Deepest)        |
|  - Bounded Line Resolution Loop (Max Attempts)        |
+-------------------------------------------------------+
                           ^
             +-------------+-------------+
             |                           |
+--------------------------+   +--------------------------+
|   TextWrapExclusions     |   |   CssFloatBandAdapter    |
|   (Documents Adapter)    |   |      (CSS Adapter)       |
|                          |   |                          |
| - DocumentShape mappings |   | - LeftAt, RightAt        |
| - Table exclusion boxes  |   | - NextBandBottom         |
| - ShapeWrap / WrapSide   |   | - DropLineBelowNarrowBands|
| - PushBelow (Deepest)    |   | - Margin box expansion   |
| - 8-attempt bounded loop |   | - Snapshot isolation     |
+--------------------------+   +--------------------------+
```

---

## 4. Verification & Performance Evidence

- **Unit and Geometry Coverage** (`ExclusionGeometryKernelTests`):
  - Valid coordinates, dimension calculations, non-finite/inverted input rejection.
  - Exact touching edge non-collision ($10^{-4}$ epsilon).
  - Zero-height query hairline evaluation at obstacle boundaries.
  - Clearance chains with cascading stacked obstacles.
  - Adversarial dense overlaps (200 overlapping full-width obstacles): verified bounded termination within 16 attempts with no runaway loops or hangs.
  - Micro-benchmarking: 100-obstacle query executes in $< 10$ microseconds per query.
- **Adapter & Integration Coverage** (`SharedLayoutExclusionsPriority2Tests`):
  - CSS float edge narrowing (`LeftAt`, `RightAt`) and shallowest bottom stepping (`NextBandBottom`, `DropLineBelowNarrowBands`).
  - Snapshot isolation verifying immutable copy semantics.
  - Document shape and table wrap equivalence.
  - End-to-end `DocumentLayout` integration with square and top-and-bottom shapes.
