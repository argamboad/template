namespace Template.Core.Abstractions;

/// <summary>
/// The single seam for storing and retrieving binary content (ADR-010). Mirrors the
/// <see cref="IEmailSender"/> shape: a Core abstraction with a config-selected Infrastructure impl
/// (local disk for dev, an S3-compatible backend in production). Features depend on this — never on a
/// cloud SDK or <c>System.IO</c> directly.
/// <para>
/// <b>Keys are tenant-scoped.</b> The implementation namespaces every object under the current tenant
/// (<c>{tenantId}/…</c>) and <b>rejects</b> keys that try to escape that namespace (traversal,
/// rooted/absolute paths) — the blob analogue of the <c>ITenantScoped</c> query filter (ADR-003). With
/// no current tenant the operation fails closed. <b>Streams, never buffers</b> whole files.
/// </para>
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores (overwriting) the object at <paramref name="key"/> with the given content type.</summary>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Opens the object for reading, or null if it does not exist. The caller disposes the result.</summary>
    Task<FileObject?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>True if the object exists for the current tenant.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes the object. Deleting a missing key is a no-op, not an error (cloud parity).</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// A readable handle to a stored object. Own it with <c>await using</c> — disposing releases the
/// underlying stream (and any file handle).
/// </summary>
public sealed record FileObject(Stream Content, string ContentType, long Length) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>Thrown when a storage key is not valid for the current tenant (empty, traversal, rooted, etc.).</summary>
public sealed class InvalidStorageKeyException(string message) : Exception(message);
