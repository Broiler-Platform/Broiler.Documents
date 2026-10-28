# Shared Layout Foundations: Fixed-Page Drawing & Graphics Bridge

This document specifies the fixed-page drawing and graphics bridge architecture connecting Layout's `DisplayList`, the engine-neutral `IPaginatedDocumentArtifact` pagination contracts, fixed-page `BPage` / `BRenderCommand` rendering, and the standalone `PdfWriter` consumer.

---

## 1. Overview & Architectural Boundaries

Fixed-page export and preview consume fully laid out, paginated documents without remeasuring fonts or re-breaking lines. The graphics pipeline bridges two producer systems into downstream consumers:

```
    ┌────────────────────────┐                   ┌────────────────────────┐
    │ Layout DisplayList     │                   │ RichTextDocument       │
    │ (HTML / CSS Renderer)  │                   │ (Office / Word / Flow) │
    └───────────┬────────────┘                   └───────────┬────────────┘
                │                                            │
                │                                            ▼
                │                               ┌────────────────────────┐
                │                               │ DocumentLayout         │
                │                               │ (Pagination Engine)    │
                │                               └───────────┬────────────┘
                │                                            │
                ▼                                            ▼
   ┌─────────────────────────┐                  ┌────────────────────────┐
   │ DisplayListBridge       │                  │ IPaginatedArtifact     │
   │ (CssPointsBridge,       │                  │ (LayoutResult)         │
   │  LIFO Clip/Transform)   │                  └───────────┬────────────┘
   └────────────┬────────────┘                              │
                │                                           │
                ▼                                           ▼
   ┌─────────────────────────┐                  ┌────────────────────────┐
   │ BPage / BRenderList     │                  │ PdfWriter / Codec      │
   │ (Fixed-Page Graphics)   │                  │ (ADR 0001/0007 Guard)  │
   └─────────────────────────┘                  └────────────────────────┘
```

### Architectural Guardrails
1. **Producer-Consumer Independence**:
   - Producers (`DocumentLayout`, `DocumentPagePainter`, CSS layout engine) resolve all geometry, font metrics, line breaks, and table topologies.
   - Consumers (`PdfWriter`, `BPageBuilder`, SVG/raster exporters) render the resolved geometric primitives and must **never** reflow content or re-measure fonts.
2. **Dependency Inversion & Delivery Guards (ADR 0001 / ADR 0007)**:
   - `Broiler.Documents.Pdf.csproj` references strictly `Broiler.Documents.csproj` and `Broiler.Documents.Model.csproj`. It contains zero third-party dependencies and does not reference `Broiler.Documents.Pagination.csproj`.
   - The shared pagination contracts live in [`Broiler.Documents.Model.Pagination`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Pagination/IPaginatedArtifacts.cs). Both `LayoutResult` and `PdfPageLayout` bind to `IPaginatedDocumentArtifact` without tight coupling.

---

## 2. DisplayList to BRenderCommand & BPage Inventory

Layout items are formally inventoried against Graphics `BRenderCommand` and `BPage` primitives. No unsupported item is dropped silently; every item is categorized, mapped, decomposed, or reported to a diagnostic sink.

### 2.1 Item Kind Catalog & Mapping Matrix ([`DisplayListInventory`](file:///d:/Broiler.Documents/src/Broiler.Documents.Pagination/Drawing/DisplayListInventory.cs))

| DisplayItem Kind | Support Level | Graphics Target | Fallback / Emulation Strategy |
| :--- | :--- | :--- | :--- |
| `FillRect` | Direct | `BRenderCommand.FillRect` | 1:1 translation with CSS-pixel to point conversion. |
| `StrokeRect` | Direct | `BRenderCommand.StrokeRect` | 1:1 translation with stroke thickness scaling. |
| `FillPolygon` | Decomposed | `BRenderCommand.FillPolygon` | Convex decomposition or triangle fan when engine lacks concave support. |
| `StrokePolygon` | Decomposed | `BRenderCommand.DrawLine` | Emitted as individual perimeter lines if polygon stroke unsupported. |
| `DrawText` | Direct | `BRenderCommand.DrawText` | Font family, size, slant, weight, baseline translation. |
| `DrawImage` | Direct | `BRenderCommand.DrawImage` | Registers owned `ReadOnlyMemory<byte>` buffer with `BPageBuilder.AddImage`. |
| `PushClip` | Direct | `BRenderCommand.PushClip` | Tracked in LIFO stack; bounds converted to page points. |
| `PopClip` | Direct | `BRenderCommand.PopClip` | Balanced with corresponding `PushClip`. |
| `PushTransform` | Direct | `BRenderCommand.PushTransform` | 2D affine matrix; tracked in LIFO stack. |
| `PopTransform` | Direct | `BRenderCommand.PopTransform` | Balanced with corresponding `PushTransform`. |
| `DrawBorder` | Decomposed | `BRenderCommand.FillRect` / `DrawLine` | Decomposed into 4 boundary edges with style arbitration. |
| `DrawBoxShadow` | Emulated | Multiple `FillRect` / `PushClip` | Soft-stepped concentric rings with decaying alpha or raster fallback. |
| `DrawLinearGradient` | Emulated | Multi-band `FillRect` | Emitted as fine-stepped solid slices (128–512 bands) or shader pattern. |
| `DrawRadialGradient`| Emulated | Multi-ring `FillRect` / `PushClip` | Emitted as concentric stepped rings or offscreen raster. |
| `DrawConicGradient` | RasterFallback | Offscreen Raster | Conic shader evaluated into clamped bitmap at bounded DPI. |
| `DrawSvgPath` | Decomposed | Linear/Cubic segments | Flattened into line and bezier curve draw primitives. |
| `PushOpacity` | Emulated | Alpha modulation | Multiplies color alpha of enclosed commands or rasterizes layer. |
| `PopOpacity` | Emulated | Restore alpha | Restores parent alpha state. |
| `PushBlendMode` | Unsupported | Ignored + Warning | Blend mode ignored; diagnostic emitted (`DrawingDiagnosticCodes.BlendModeUnsupported`). |
| `PopBlendMode` | Unsupported | No-op | Diagnostics logged; balance preserved. |
| `PushFilter` | RasterFallback | Offscreen Raster | Blur/drop-shadow/matrix filter rasterized at bounded resolution. |
| `PopFilter` | RasterFallback | Blit Raster | Composites filtered raster buffer back to target page. |
| `DrawRule` | Direct | `BRenderCommand.StrokeRect` / `DrawLine` | Degenerate hairline rectangle or line segment. |

---

## 3. Fixed-Page Bridge & Coordinate Systems

### 3.1 Unit Conversion ([`CssPointsBridge`](file:///d:/Broiler.Documents/src/Broiler.Documents.Pagination/Drawing/CssPointsBridge.cs))
- Typographical points: $72 \text{ points per inch}$.
- CSS pixels: $96 \text{ pixels per inch}$.
- Ratio:
  $$\text{Points} = \text{CssPixels} \times \frac{72}{96} = \text{CssPixels} \times 0.75$$
  $$\text{CssPixels} = \text{Points} \times \frac{96}{72} = \text{Points} \times 1.333333...$$

All layout coordinates entering `DisplayListBridge` are explicitly converted:
```csharp
double ptX = CssPointsBridge.CssPixelsToPoints(cssX);
double ptY = CssPointsBridge.CssPixelsToPoints(cssY);
```

### 3.2 LIFO Stack Balancing ([`DisplayListBridge`](file:///d:/Broiler.Documents/src/Broiler.Documents.Pagination/Drawing/DisplayListBridge.cs))
Unbalanced clips or transforms can corrupt subsequent pages. `DisplayListBridge` maintains a strict LIFO layer stack (`Stack<LayerKind>`):
1. **Push**: Pushing a clip or transform pushes `LayerKind.Clip` or `LayerKind.Transform` onto the stack, tracking recursion depth.
2. **Pop**: Popping validates that the topmost layer matches the requested pop operation. If a mismatch or underflow occurs, an error diagnostic (`DrawingDiagnosticCodes.StackUnderflow`) is emitted.
3. **Safety Unwind**: At the end of a page render, any unpopped clips or transforms are automatically popped in reverse order to ensure clean graphics state isolation.

### 3.3 Image Ownership & Handle Deduplication
Images passed across the bridge retain their owned byte buffers in `BPageBuilder.AddImage`:
- Repeated images (by byte equality or SHA-256 fingerprint) are deduplicated into single `BImageHandle` objects.
- Image memory is owned by the container document or page builder; disposal of temporary layout structures does not invalidate native rendering resources.

---

## 4. Detached Shaping Manifest & Fallback Policy

### 4.1 Surviving Producer Disposal ([`PageShapingManifest`](file:///d:/Broiler.Documents/src/Broiler.Documents.Pagination/Drawing/PageShapingManifest.cs))
Producer layout passes resolve complex fonts, glyph mappings, and cluster positions. Once pagination completes, layout objects may be collected. `PageShapingManifest` extracts and stores immutable, detached shaping artifacts:
- `FontFaceIdentity`: Font family, subfamily, PostScript name, program ID, face index, and embedded flag.
- `ShapedCluster`: Source run text offsets, advance widths, cluster bounds, and resolved glyph IDs.
- `ShapedPageRun`: Baseline-anchored position, font identity, and glyph sequence.

Consumers can inspect or serialize text without retaining large layout ASTs or host font engine handles.

### 4.2 Fallback Rasterization Limits
When complex graphics effects (filters, conic gradients, SVG blend modes) require rasterization:
- **Maximum Raster Dimension**: $4,096 \text{ px}$ (`DisplayListInventory.MaxRasterDimension`). Prevents allocation bombs from oversized SVG viewports.
- **Default Raster Resolution**: $300 \text{ DPI}$ (`DisplayListInventory.DefaultRasterDpi`). High-fidelity print quality.
- **Screen Raster Resolution**: $96 \text{ DPI}$ (`DisplayListInventory.ScreenDpi`). Interactive display.
- **Maximum Stack Depth**: $64$ (`DisplayListInventory.MaxClipDepth`, `DisplayListInventory.MaxTransformDepth`). Protects against recursive stack overflows.

---

## 5. Documents / PDF Pagination Migration

Independent PDF line breaking and measurement in `PdfPageLayout` has been migrated to consume the engine-neutral pagination artifact (`IPaginatedDocumentArtifact` / `LayoutResult`).

### 5.1 Shared Contracts ([`Broiler.Documents.Model.Pagination`](file:///d:/Broiler.Documents/src/Broiler.Documents.Model/Pagination/IPaginatedArtifacts.cs))
- `IPaginatedDocumentArtifact`: Resolves total pages and reading order.
- `IPaginatedPageArtifact`: Resolves page geometry (`WidthPoints`, `HeightPoints`), background fills, lines, and shapes.
- `IPaginatedLineArtifact`: Resolves line top, height, baseline, paragraph index, and child pieces.
- `IPaginatedPieceArtifact`: Resolves piece text, bounds, font family, font size, bold/italic traits, colors, highlights, and link targets.
- `IPaginatedShapeArtifact`: Resolves floating shape bounds, geometry, fills, outlines, and border widths.

### 5.2 Coordinate Inversion: Top-Left to Bottom-Left
In `DocumentLayout` and `BPage`, coordinates start at top-left $(0, 0)$ with $Y$ increasing downward. In PDF user space, coordinates start at bottom-left $(0, 0)$ with $Y$ increasing upward.
The transformation rules are:
```csharp
// Shapes
double pdfShapeY = pageHeight - (shape.Y + shape.Height);

// Text Baseline
double pdfBaseline = pageHeight - line.Baseline;
```

### 5.3 PDF Link Policy & URI Validation
Every link in `IPaginatedPieceArtifact.Link` is revalidated against `PdfUriPolicy`:
- Under `PdfUriPolicy.Default`, only absolute `https://` URIs are emitted as PDF `/Link` annotations.
- Unpermitted schemes (`http`, `mailto`, `file`, `javascript`) are stripped from annotation emission and recorded as `PdfDiagnosticCodes.UriRejected` without dropping the underlying text.

---

## 6. Verification & Test Suite

The fixed-page drawing and graphics bridge architecture is covered by automated unit and end-to-end integration test suites:

1. **Drawing & DisplayList Bridge Unit Tests** ([`SharedLayoutDrawingPriority5Tests.cs`](file:///d:/Broiler.Documents/src/tests/Broiler.Documents.Pagination.Tests/SharedLayoutDrawingPriority5Tests.cs)):
   - Verifies DisplayList inventory completeness across all 23 items.
   - Tests CSS-to-point unit conversion accuracy.
   - Validates border decomposition into rectangular fills and strokes.
   - Tests automatic stack balancing for unclosed clips and transforms.
   - Verifies image deduplication and ownership handling.
   - Tests `PageShapingManifest` detachment and serialization.
2. **PDF Pagination Bridge Unit Tests** ([`SharedLayoutPdfPaginationPriority5Tests.cs`](file:///d:/Broiler.Documents/src/tests/Broiler.Documents.Pdf.Tests/SharedLayoutPdfPaginationPriority5Tests.cs)):
   - Verifies `PdfPageLayout.BuildFromArtifact` coordinate inversions.
   - Verifies font selection and URI policy rejection diagnostics.
   - Verifies multi-page document pagination without remeasuring.
3. **End-to-End Layout to PDF Pipeline Tests** ([`SharedLayoutPdfEndToEndPriority5Tests.cs`](file:///d:/Broiler.Documents/src/tests/Broiler.Documents.Cli.Tests/SharedLayoutPdfEndToEndPriority5Tests.cs)):
   - Full roundtrip: `DocumentLayout.Layout` $\to$ `LayoutResult` $\to$ `PdfDocumentCodec.WritePdf` $\to$ `PdfDocumentCodec.ReadPdf`.
   - Validates multi-page splitting, exact text extraction, floating shapes, and hyperlink preservation.
