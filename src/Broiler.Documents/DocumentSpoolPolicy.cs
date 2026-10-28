using System;
using System.IO;

namespace Broiler.Documents;

/// <summary>
/// A host-supplied policy governing how a <see cref="DocumentInput"/> spools
/// large or non-seekable document sources: target directory, quota bounds,
/// in-memory threshold, cleanup, and crash recovery.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing is ambient.</strong> Broiler never writes document bytes to
/// <see cref="Path.GetTempPath"/> or an unmanaged temporary location with no
/// agreed privacy, retention, or quota rules. Spooling is strictly opt-in and
/// requires the host application to explicitly provision this policy with a
/// designated directory and quotas.
/// </para>
/// <para>
/// <strong>Memory threshold.</strong> Sources whose size does not exceed
/// <see cref="MemoryThresholdBytes"/> remain entirely in memory and never touch
/// disk, avoiding disk I/O for ordinary-sized documents.
/// </para>
/// </remarks>
public sealed class DocumentSpoolPolicy
{
    /// <summary>Default maximum bytes spooled to disk or memory (64 MB).</summary>
    public const long DefaultMaxSpoolBytes = 64L * 1024 * 1024;

    /// <summary>Default in-memory threshold before spooling to disk begins (64 KB).</summary>
    public const int DefaultMemoryThresholdBytes = 64 * 1024;

    /// <summary>Default filename prefix for spool files.</summary>
    public const string DefaultFilePrefix = "broiler-spool-";

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentSpoolPolicy"/>.
    /// </summary>
    /// <param name="directoryPath">
    /// An existing directory path where spool files may be created. Cannot be null or whitespace.
    /// </param>
    /// <param name="maxSpoolBytes">
    /// Hard quota for the maximum bytes that may be spooled. Exceeding this limit aborts the spool.
    /// Defaults to <see cref="DefaultMaxSpoolBytes"/> (64 MB).
    /// </param>
    /// <param name="memoryThresholdBytes">
    /// Maximum bytes retained in memory before creating a spool file on disk.
    /// Defaults to <see cref="DefaultMemoryThresholdBytes"/> (64 KB). Setting 0 forces immediate disk spooling.
    /// </param>
    /// <param name="filePrefix">
    /// Prefix for spool filenames in <paramref name="directoryPath"/>. Defaults to <see cref="DefaultFilePrefix"/>.
    /// </param>
    /// <param name="deleteOnDispose">
    /// When true (the default), the temporary spool file is deleted when the <see cref="DocumentInput"/> is disposed.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="directoryPath"/> is null or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSpoolBytes"/> is negative or zero, or <paramref name="memoryThresholdBytes"/> is negative.</exception>
    public DocumentSpoolPolicy(
        string directoryPath,
        long maxSpoolBytes = DefaultMaxSpoolBytes,
        int memoryThresholdBytes = DefaultMemoryThresholdBytes,
        string? filePrefix = null,
        bool deleteOnDispose = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"The spool directory does not exist: '{directoryPath}'. An explicit host directory is required.");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpoolBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(memoryThresholdBytes);

        DirectoryPath = directoryPath;
        MaxSpoolBytes = maxSpoolBytes;
        MemoryThresholdBytes = memoryThresholdBytes;
        FilePrefix = string.IsNullOrWhiteSpace(filePrefix) ? DefaultFilePrefix : filePrefix;
        DeleteOnDispose = deleteOnDispose;
    }

    /// <summary>The designated directory path where spool files are created.</summary>
    public string DirectoryPath { get; }

    /// <summary>The hard ceiling on total bytes that may be spooled.</summary>
    public long MaxSpoolBytes { get; }

    /// <summary>Bytes buffered in memory before spooling to disk begins.</summary>
    public int MemoryThresholdBytes { get; }

    /// <summary>Filename prefix for spool files created in <see cref="DirectoryPath"/>.</summary>
    public string FilePrefix { get; }

    /// <summary>Whether to delete the temporary spool file when the input is disposed.</summary>
    public bool DeleteOnDispose { get; }

    /// <summary>
    /// Purges stale or orphaned spool files matching <see cref="FilePrefix"/> in <see cref="DirectoryPath"/>
    /// that are older than <paramref name="olderThan"/>, enabling recovery from crashed host processes.
    /// </summary>
    /// <param name="olderThan">
    /// Minimum age threshold for deletion. Defaults to 1 hour if not specified.
    /// </param>
    /// <returns>The number of orphaned spool files successfully deleted.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="olderThan"/> is negative.</exception>
    public int CleanOrphanedFiles(TimeSpan? olderThan = null)
    {
        TimeSpan age = olderThan ?? TimeSpan.FromHours(1);
        if (age < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(olderThan), "Age threshold cannot be negative.");

        DateTime cutoff = DateTime.UtcNow - age;
        int deleted = 0;

        if (!Directory.Exists(DirectoryPath))
            return 0;

        var dirInfo = new DirectoryInfo(DirectoryPath);
        foreach (FileInfo file in dirInfo.EnumerateFiles(FilePrefix + "*.spool"))
        {
            try
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    file.Delete();
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File is currently locked by another process or permissions forbid deletion.
            }
        }

        return deleted;
    }
}
