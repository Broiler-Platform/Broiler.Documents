namespace Broiler.Documents.Model.Exclusions;

/// <summary>
/// Dictates how an obstacle interacts with lines of text attempting to share its vertical slice.
/// </summary>
public enum ObstacleWrapKind
{
    /// <summary>The obstacle allows text to run through or behind/in front of it without exclusion.</summary>
    None = 0,

    /// <summary>
    /// The obstacle prevents any text beside it across the whole band (e.g. Word wrapTopAndBottom,
    /// CSS clear: both, or full-width clearance). Lines must drop past its bottom edge.
    /// </summary>
    ClearanceOnly = 1,

    /// <summary>
    /// The obstacle restricts text to one or both sides (e.g. Word wrapSquare, tight text wrapping).
    /// </summary>
    SideRestricted = 2,

    /// <summary>
    /// Dedicated CSS float: left obstacle. Pushes the container's left edge to its right margin edge.
    /// </summary>
    CssFloatLeft = 3,

    /// <summary>
    /// Dedicated CSS float: right obstacle. Pulls the container's right edge to its left margin edge.
    /// </summary>
    CssFloatRight = 4,
}

/// <summary>
/// For a <see cref="ObstacleWrapKind.SideRestricted"/> obstacle, specifies which side text may occupy.
/// </summary>
public enum ObstacleSidePolicy
{
    /// <summary>Text occupies whichever side has the greater available room. Defaults to left on tie.</summary>
    Largest = 0,

    /// <summary>Text occupies only the space to the left of the obstacle (obstacle is on the right).</summary>
    LeftOnly = 1,

    /// <summary>Text occupies only the space to the right of the obstacle (obstacle is on the left).</summary>
    RightOnly = 2,

    /// <summary>Text may occupy available bands on both sides of the obstacle.</summary>
    BothSides = 3,

    /// <summary>CSS float semantics: left floats push left edge, right floats pull right edge.</summary>
    CssFloats = 4,
}

/// <summary>
/// Strategy used to advance the vertical position Y when a line cannot fit at the current Y.
/// </summary>
public enum ClearanceDescentStep
{
    /// <summary>
    /// Drops below the deepest bottom of all overlapping blocking obstacles (Word PushBelow style).
    /// </summary>
    Deepest = 0,

    /// <summary>
    /// Drops below the shallowest bottom of overlapping obstacles (CSS NextBandBottom style, CSS2.1 §9.5).
    /// Widens the band incrementally as each float clears.
    /// </summary>
    Shallowest = 1,
}

/// <summary>
/// Fallback action when a line cannot find a usable band within the allowed retry steps.
/// </summary>
public enum BlockedBandFallback
{
    /// <summary>
    /// Line takes the full container width at the current Y and draws through (visible and finite).
    /// </summary>
    FullColumnAtCurrentY = 0,

    /// <summary>Line takes the full container width at the starting Y before clearance was attempted.</summary>
    FullColumnAtInitialY = 1,

    /// <summary>Returns a zero-width band, signalling the caller that no room was found.</summary>
    ZeroWidth = 2,
}

/// <summary>
/// Determines how multiple intersecting obstacles at the same vertical slice are combined into available bands.
/// </summary>
public enum MultiObstaclePolicy
{
    /// <summary>
    /// Process obstacles sequentially in order, clamping the remaining span step-by-step
    /// (exact legacy Word/Writer TextWrapExclusions behavior).
    /// </summary>
    SequentialClamping = 0,

    /// <summary>
    /// Compute all disjoint free intervals by subtracting all obstacles from the container span,
    /// selecting according to <see cref="ObstacleSidePolicy"/>.
    /// </summary>
    GlobalIntervals = 1,
}
