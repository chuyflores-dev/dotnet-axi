using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.Structural;

internal static class SymbolEntityIdentity
{
    private const string Prefix = "symbol/v2/";

    public static string Create(
        string name,
        string kind,
        string fullyQualifiedName,
        string signature,
        string contentHash,
        int spanStart,
        int spanLength,
        string relativePath,
        bool isExternal,
        IReadOnlyList<FileCompilerVariant> variants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(variants);
        using var stableHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        StructuralCandidateIdentity.Append(stableHash, "dotnet-axi/symbol-entity-stable/v2");
        StructuralCandidateIdentity.Append(stableHash, kind);
        StructuralCandidateIdentity.Append(stableHash, fullyQualifiedName);
        StructuralCandidateIdentity.Append(stableHash, signature);
        StructuralCandidateIdentity.Append(stableHash, contentHash);
        StructuralCandidateIdentity.Append(stableHash, spanStart.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        StructuralCandidateIdentity.Append(stableHash, spanLength.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        StructuralCandidateIdentity.Append(
            stableHash,
            variants.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        foreach (var variant in variants)
        {
            StructuralCandidateIdentity.Append(stableHash, variant.Project);
            StructuralCandidateIdentity.Append(
                stableHash,
                variant.Configuration ?? string.Empty);
            StructuralCandidateIdentity.Append(
                stableHash,
                variant.Framework ?? string.Empty);
            StructuralCandidateIdentity.Append(
                stableHash,
                variant.ContextFingerprint);
        }

        using var locationHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        StructuralCandidateIdentity.Append(locationHash, "dotnet-axi/symbol-entity-location/v2");
        StructuralCandidateIdentity.Append(
            locationHash,
            relativePath.Replace('\\', '/'));
        StructuralCandidateIdentity.Append(
            locationHash,
            isExternal ? "external" : "workspace");
        return Prefix
            + Base64Url(Encoding.UTF8.GetBytes(name))
            + "/"
            + Convert.ToHexStringLower(stableHash.GetHashAndReset())
            + "/"
            + Convert.ToHexStringLower(locationHash.GetHashAndReset());
    }

    public static bool TryParse(string id, out SymbolEntityIdentityParts parts)
    {
        parts = default!;
        if (string.IsNullOrWhiteSpace(id)
            || !id.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = id[Prefix.Length..];
        var segments = remainder.Split('/');
        if (segments.Length != 3
            || segments[0].Length == 0
            || !IsLowerHex(segments[1], 64)
            || !IsLowerHex(segments[2], 64))
        {
            return false;
        }

        var encodedName = segments[0];
        try
        {
            var bytes = FromBase64Url(encodedName);
            var name = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
            if (string.IsNullOrWhiteSpace(name)
                || name.Contains('\0', StringComparison.Ordinal)
                || !Base64Url(bytes).Equals(encodedName, StringComparison.Ordinal))
            {
                return false;
            }

            parts = new SymbolEntityIdentityParts(name, segments[1], segments[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = base64.Length % 4;
        if (padding == 1)
        {
            throw new FormatException("Invalid base64url length.");
        }

        if (padding > 0)
        {
            base64 = base64.PadRight(base64.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(base64);
    }

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length
        && value.All(static character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');
}

internal sealed record SymbolEntityIdentityParts(
    string LookupName,
    string StableFingerprint,
    string LocationFingerprint);

public sealed record SymbolEntityResolution
{
    public SymbolEntityResolution(
        string id,
        string lookupName,
        string snapshot,
        int observedFileCount,
        IEnumerable<SymbolDeclarationMatch> matches,
        IEnumerable<SymbolDeclarationMatch>? replacementCandidates = null,
        string? errorCode = null,
        string? query = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(lookupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentOutOfRangeException.ThrowIfNegative(observedFileCount);
        Id = id;
        LookupName = lookupName;
        Snapshot = snapshot;
        ObservedFileCount = observedFileCount;
        Matches = Array.AsReadOnly(matches.ToArray());
        ReplacementCandidates = Array.AsReadOnly(
            replacementCandidates?.ToArray() ?? []);
        ErrorCode = errorCode;
        Query = query;
        if ((errorCode is null) != (query is null)
            || (errorCode is null && ReplacementCandidates.Count > 0)
            || (errorCode is not null && Matches.Count > 0))
        {
            throw new ArgumentException(
                "Stale symbol resolution must carry only an error, query, and replacement candidates.");
        }
    }

    public string Id { get; }

    public string LookupName { get; }

    public string Snapshot { get; }

    public int ObservedFileCount { get; }

    public IReadOnlyList<SymbolDeclarationMatch> Matches { get; }

    public IReadOnlyList<SymbolDeclarationMatch> ReplacementCandidates { get; }

    public string? ErrorCode { get; }

    public string? Query { get; }

    public bool Stale => ErrorCode is not null;

    public bool Resolved => !Stale && Matches.Count == 1;

    public bool Ambiguous => !Stale && Matches.Count > 1;
}

/// <summary>
/// Resolves a versioned symbol entity ID by scanning the supplied traversal.
/// Resolution does not read or require a daemon, cache, database, or user
/// state.
/// </summary>
public sealed class SymbolEntityResolver
{
    private readonly SymbolDeclarationSearcher _searcher;

    public SymbolEntityResolver(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownership)
    {
        _searcher = new SymbolDeclarationSearcher(traverser, ownership);
    }

    public static bool IsSupportedId(string id) =>
        SymbolEntityIdentity.TryParse(id, out _);

    public async ValueTask<SymbolEntityResolution> ResolveAsync(
        string id,
        WorkspaceTraversalRequest traversal,
        CancellationToken cancellationToken = default)
        => await ResolveAsync(
            id,
            traversal,
            includeTests: true,
            includeGenerated: true,
            cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<SymbolEntityResolution> ResolveAsync(
        string id,
        WorkspaceTraversalRequest traversal,
        bool includeTests,
        bool includeGenerated,
        CancellationToken cancellationToken = default)
        => await ResolveAsync(
            id,
            traversal,
            new SymbolDeclarationScope(
                solution: null,
                projects: null,
                traversal.ExplicitPaths,
                includeTests,
                includeGenerated),
            cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<SymbolEntityResolution> ResolveAsync(
        string id,
        WorkspaceTraversalRequest traversal,
        SymbolDeclarationScope scope,
        CancellationToken cancellationToken = default)
    {
        if (!SymbolEntityIdentity.TryParse(id, out var identity))
        {
            throw new ArgumentException(
                "The symbol entity ID is not a supported canonical symbol/v2 identity.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(traversal);
        cancellationToken.ThrowIfCancellationRequested();
        var resolutionTraversal = new WorkspaceTraversalRequest(
            traversal.WorkspaceRoot,
            traversal.Configuration,
            traversal.ExplicitPaths,
            scope.IncludeGenerated,
            currentDirectory: traversal.CurrentDirectory);
        var result = await _searcher.SearchAsync(
            new SymbolDeclarationSearchRequest(
                identity.LookupName,
                resolutionTraversal,
                includeTests: scope.IncludeTests,
                scope: scope),
            cancellationToken)
            .ConfigureAwait(false);
        var stableMatches = result.Matches
            .Where(match => HasStableFingerprint(
                match,
                identity.StableFingerprint))
            .ToArray();
        var exactMatches = stableMatches
            .Where(match => match.Id.Equals(id, StringComparison.Ordinal)
                || match.LegacyId?.Equals(id, StringComparison.Ordinal) == true)
            .ToArray();
        var matches = exactMatches.Length > 0 ? exactMatches : stableMatches;
        if (matches.Length > 0)
        {
            return new SymbolEntityResolution(
                id,
                identity.LookupName,
                result.Snapshot,
                result.Observations.Count,
                matches);
        }

        return new SymbolEntityResolution(
            id,
            identity.LookupName,
            result.Snapshot,
            result.Observations.Count,
            [],
            result.Matches,
            "evidence.stale_id",
            ReplacementQuery(identity.LookupName));
    }

    private static bool HasStableFingerprint(
        SymbolDeclarationMatch match,
        string stableFingerprint) =>
        HasStableFingerprint(match.Id, stableFingerprint)
        || match.LegacyId is not null
        && HasStableFingerprint(match.LegacyId, stableFingerprint);

    private static bool HasStableFingerprint(
        string id,
        string stableFingerprint) =>
        SymbolEntityIdentity.TryParse(id, out var candidate)
        && candidate.StableFingerprint.Equals(
            stableFingerprint,
            StringComparison.Ordinal);

    private static string ReplacementQuery(string name) =>
        "dnaxi search symbol "
        + Quote(name)
        + " --fields 'id,signature,owning_projects,variant_count,variants' --full";

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
