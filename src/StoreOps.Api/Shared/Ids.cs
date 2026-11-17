namespace StoreOps.Api.Shared;

public static class Ids
{
    /// <summary>
    /// Creates a prefixed identifier such as <c>act-3f2a9c1b…</c> with the full 128-bit GUID, because
    /// repositories upsert by id and a collision would silently overwrite another record.
    /// </summary>
    public static string New(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
