using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;

namespace DotNetAxi.Structural;

internal static class SymbolEntityIdentity
{
    private const string Prefix = "symbol/v1/";

    public static string Create(
        string name,
        string kind,
        string fullyQualifiedName,
        string signature,
        string contentHash,
        int spanStart,
        int spanLength,
        string relativePath,
        bool isExternal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var stableHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        StructuralCandidateIdentity.Append(stableHash, "dotnet-axi/symbol-entity-stable/v1");
        StructuralCandidateIdentity.Append(stableHash, kind);
        StructuralCandidateIdentity.Append(stableHash, fullyQualifiedName);
        StructuralCandidateIdentity.Append(stableHash, signature);
        StructuralCandidateIdentity.Append(stableHash, contentHash);
        StructuralCandidateIdentity.Append(stableHash, spanStart.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        StructuralCandidateIdentity.Append(stableHash, spanLength.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        using var locationHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        StructuralCandidateIdentity.Append(locationHash, "dotnet-axi/symbol-entity-location/v1");
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
        IEnumerable<SymbolDeclarationMatch> matches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(matches);
        Id = id;
        Matches = Array.AsReadOnly(matches.ToArray());
    }

    public string Id { get; }

    public IReadOnlyList<SymbolDeclarationMatch> Matches { get; }

    public bool Resolved => Matches.Count == 1;

    public bool Ambiguous => Matches.Count > 1;
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

    public async ValueTask<SymbolEntityResolution> ResolveAsync(
        string id,
        WorkspaceTraversalRequest traversal,
        CancellationToken cancellationToken = default)
    {
        if (!SymbolEntityIdentity.TryParse(id, out var identity))
        {
            throw new ArgumentException(
                "The symbol entity ID is not a supported canonical symbol/v1 identity.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(traversal);
        cancellationToken.ThrowIfCancellationRequested();
        var resolutionTraversal = new WorkspaceTraversalRequest(
            traversal.WorkspaceRoot,
            traversal.Configuration,
            traversal.ExplicitPaths,
            includeGenerated: true,
            currentDirectory: traversal.CurrentDirectory);
        var result = await _searcher.SearchAsync(
            new SymbolDeclarationSearchRequest(
                identity.LookupName,
                resolutionTraversal,
                includeTests: true),
            cancellationToken)
            .ConfigureAwait(false);
        var stableMatches = result.Matches
            .Where(match => SymbolEntityIdentity.TryParse(match.Id, out var candidate)
                && candidate.StableFingerprint.Equals(
                    identity.StableFingerprint,
                    StringComparison.Ordinal))
            .ToArray();
        var exactMatches = stableMatches
            .Where(match => match.Id.Equals(id, StringComparison.Ordinal))
            .ToArray();
        return new SymbolEntityResolution(
            id,
            exactMatches.Length > 0 ? exactMatches : stableMatches);
    }
}
