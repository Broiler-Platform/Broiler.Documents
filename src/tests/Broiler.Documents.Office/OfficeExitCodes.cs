using System;

namespace Broiler.Documents.Office;

internal static class OfficeExitCodes
{
    // Reserve 99 for usage errors and harness failures. The full comparison
    // failure count remains in the log and reports.
    public static int ForFailures(int failed) => Math.Min(failed, 98);
}
