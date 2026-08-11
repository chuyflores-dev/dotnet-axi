using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotNetAxi.Structural;

public sealed record SourceOutline(
    string Path,
    bool External,
    int DiagnosticCount,
    int TotalCount,
    IReadOnlyList<SourceOutlineItem> Items);

public sealed record SourceOutlineItem(
    string Id,
    string Kind,
    string? Name,
    string Signature,
    IReadOnlyList<string> Attributes,
    int Depth,
    StructuralSourceRange Range);

/// <summary>
/// Produces a compact declaration outline from C# syntax without creating a
/// compilation, evaluating a project, or executing repository code.
/// </summary>
public sealed class RoslynSourceOutliner
{
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public SourceOutline OutlineDocument(
        string path,
        bool isExternal,
        string source,
        string contentHash,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateMaxItems(maxItems);
        return Outline(
            path,
            isExternal,
            SourceText.From(
                source,
                DefaultEncoding,
                SourceHashAlgorithm.Sha256),
            RequiredContentHash(contentHash),
            selectedSpan: null,
            maxItems,
            cancellationToken);
    }

    public SourceOutline OutlineSymbol(
        SymbolDeclarationMatch match,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        ValidateMaxItems(maxItems);
        if (match.SourceBytes.Length == 0)
        {
            throw new ArgumentException(
                "The symbol match does not carry source resolution metadata.",
                nameof(match));
        }

        var source = SourceText.From(
            match.SourceBytes,
            match.SourceBytes.Length,
            DefaultEncoding,
            SourceHashAlgorithm.Sha256,
            throwIfBinaryDetected: true,
            canBeEmbedded: false);
        return Outline(
            match.Range.Start.Path,
            match.Range.Start.IsExternal,
            source,
            Convert.ToHexStringLower(SHA256.HashData(match.SourceBytes)),
            new TextSpan(match.SourceSpanStart, match.SourceSpanLength),
            maxItems,
            cancellationToken);
    }

    private static SourceOutline Outline(
        string path,
        bool isExternal,
        SourceText source,
        string contentHash,
        TextSpan? selectedSpan,
        int? maxItems,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default,
            path,
            cancellationToken);
        var root = (CompilationUnitSyntax)tree.GetRoot(cancellationToken);
        var items = new List<SourceOutlineItem>();
        var totalCount = 0;
        var itemLimit = maxItems ?? int.MaxValue;

        if (selectedSpan is { } span)
        {
            var selected = root.FindNode(span, getInnermostNodeForTie: true);
            if (selected.Span != span)
            {
                throw new InvalidOperationException(
                    "The resolved declaration no longer matches its source span.");
            }

            Add(selected, depth: 0, tree, path, isExternal, contentHash,
                itemLimit, items, ref totalCount, cancellationToken);
        }
        else
        {
            foreach (var node in RootItems(root))
            {
                Add(node, depth: 0, tree, path, isExternal, contentHash,
                    itemLimit, items, ref totalCount, cancellationToken);
            }
        }

        return new SourceOutline(
            new SourceLocation(path, 1, 1, isExternal).Path,
            isExternal,
            tree.GetDiagnostics(cancellationToken).Count(),
            totalCount,
            Array.AsReadOnly(items.ToArray()));
    }

    private static void Add(
        SyntaxNode node,
        int depth,
        SyntaxTree tree,
        string path,
        bool isExternal,
        string contentHash,
        int itemLimit,
        ICollection<SourceOutlineItem> items,
        ref int totalCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = Describe(node);
        if (descriptor is null)
        {
            return;
        }

        totalCount = checked(totalCount + 1);
        if (items.Count < itemLimit)
        {
            var signature = Signature(node);
            var lineSpan = tree.GetLineSpan(node.Span, cancellationToken).Span;
            var range = new StructuralSourceRange(
                SourceLocation.FromZeroBasedUtf16(
                    path,
                    lineSpan.Start.Line,
                    lineSpan.Start.Character,
                    isExternal),
                SourceLocation.FromZeroBasedUtf16(
                    path,
                    lineSpan.End.Line,
                    lineSpan.End.Character,
                    isExternal));
            items.Add(new SourceOutlineItem(
                StructuralCandidateIdentity.Create(
                    "outline-item",
                    "outline/v1",
                    contentHash,
                    path,
                    isExternal,
                    node.SpanStart,
                    node.Span.Length,
                    descriptor.Value.Kind + "\n" + signature),
                descriptor.Value.Kind,
                descriptor.Value.Name,
                signature,
                Array.AsReadOnly(Attributes(node).ToArray()),
                depth,
                range));
        }

        foreach (var child in Children(node))
        {
            Add(child, checked(depth + 1), tree, path, isExternal, contentHash,
                itemLimit, items, ref totalCount, cancellationToken);
        }
    }

    private static IEnumerable<SyntaxNode> RootItems(CompilationUnitSyntax root) =>
        root.AttributeLists.Cast<SyntaxNode>()
            .Concat(root.Externs)
            .Concat(root.Usings)
            .Concat(root.Members)
            .OrderBy(static node => node.SpanStart);

    private static IEnumerable<SyntaxNode> Children(SyntaxNode node) =>
        node switch
        {
            BaseNamespaceDeclarationSyntax declaration =>
                declaration.Externs.Cast<SyntaxNode>()
                    .Concat(declaration.Usings)
                    .Concat(declaration.Members)
                    .OrderBy(static child => child.SpanStart),
            TypeDeclarationSyntax declaration =>
                declaration.Members.Cast<SyntaxNode>(),
            EnumDeclarationSyntax declaration =>
                declaration.Members.Cast<SyntaxNode>(),
            _ => [],
        };

    private static (string Kind, string? Name)? Describe(SyntaxNode node) =>
        node switch
        {
            AttributeListSyntax value =>
                ("attribute", value.Target?.Identifier.ValueText),
            ExternAliasDirectiveSyntax value =>
                ("extern-alias", value.Identifier.ValueText),
            UsingDirectiveSyntax value =>
                ("import", value.Alias?.Name.Identifier.ValueText
                    ?? value.Name?.ToString()),
            BaseNamespaceDeclarationSyntax value =>
                ("namespace", value.Name.ToString()),
            ClassDeclarationSyntax value =>
                ("class", value.Identifier.ValueText),
            StructDeclarationSyntax value =>
                ("struct", value.Identifier.ValueText),
            InterfaceDeclarationSyntax value =>
                ("interface", value.Identifier.ValueText),
            RecordDeclarationSyntax value =>
                ("record", value.Identifier.ValueText),
            EnumDeclarationSyntax value =>
                ("enum", value.Identifier.ValueText),
            DelegateDeclarationSyntax value =>
                ("delegate", value.Identifier.ValueText),
            MethodDeclarationSyntax value =>
                ("method", value.Identifier.ValueText),
            ConstructorDeclarationSyntax value =>
                ("constructor", value.Identifier.ValueText),
            DestructorDeclarationSyntax value =>
                ("destructor", "~" + value.Identifier.ValueText),
            PropertyDeclarationSyntax value =>
                ("property", value.Identifier.ValueText),
            IndexerDeclarationSyntax =>
                ("indexer", "this"),
            EventDeclarationSyntax value =>
                ("event", value.Identifier.ValueText),
            EventFieldDeclarationSyntax value =>
                ("event", VariableNames(value.Declaration)),
            FieldDeclarationSyntax value =>
                ("field", VariableNames(value.Declaration)),
            VariableDeclaratorSyntax value when
                value.Parent?.Parent is EventFieldDeclarationSyntax =>
                ("event", value.Identifier.ValueText),
            VariableDeclaratorSyntax value =>
                ("field", value.Identifier.ValueText),
            EnumMemberDeclarationSyntax value =>
                ("enum-member", value.Identifier.ValueText),
            OperatorDeclarationSyntax value =>
                ("operator", "operator" + value.OperatorToken.ValueText),
            ConversionOperatorDeclarationSyntax value =>
                ("conversion-operator",
                    value.ImplicitOrExplicitKeyword.ValueText + " operator"),
            GlobalStatementSyntax value when
                value.Statement is LocalFunctionStatementSyntax local =>
                ("local-function", local.Identifier.ValueText),
            GlobalStatementSyntax value =>
                ("top-level-statement", SyntaxKindName(value.Statement.Kind())),
            IncompleteMemberSyntax =>
                ("incomplete-member", null),
            _ => null,
        };

    private static string VariableNames(VariableDeclarationSyntax declaration) =>
        string.Join(',', declaration.Variables.Select(
            static variable => variable.Identifier.ValueText));

    private static IEnumerable<string> Attributes(SyntaxNode node)
    {
        var lists = node switch
        {
            VariableDeclaratorSyntax value =>
                value.Ancestors().OfType<MemberDeclarationSyntax>()
                    .FirstOrDefault()?.AttributeLists ?? [],
            MemberDeclarationSyntax value => value.AttributeLists,
            _ => [],
        };
        return lists.Select(static list => Normalize(list));
    }

    private static string Signature(SyntaxNode node) =>
        node switch
        {
            GlobalStatementSyntax value when
                value.Statement is LocalFunctionStatementSyntax local =>
                Normalize(SignatureRewriter.Instance.Visit(local)!),
            GlobalStatementSyntax value => SyntaxKindName(value.Statement.Kind()),
            VariableDeclaratorSyntax value => VariableSignature(value),
            _ => Normalize(SignatureRewriter.Instance.Visit(node) ?? node),
        };

    private static string VariableSignature(VariableDeclaratorSyntax variable)
    {
        if (variable.Parent is not VariableDeclarationSyntax declaration
            || declaration.Parent is not BaseFieldDeclarationSyntax field)
        {
            return Normalize(variable.WithInitializer(null));
        }

        var selected = declaration.WithVariables(
            SyntaxFactory.SingletonSeparatedList(variable.WithInitializer(null)));
        return Normalize(field switch
        {
            FieldDeclarationSyntax value => value
                .WithAttributeLists([])
                .WithDeclaration(selected),
            EventFieldDeclarationSyntax value => value
                .WithAttributeLists([])
                .WithDeclaration(selected),
            _ => field,
        });
    }

    private static string Normalize(SyntaxNode node) =>
        node.WithoutTrivia()
            .NormalizeWhitespace(indentation: " ", eol: " ")
            .ToFullString()
            .Trim();

    private static string SyntaxKindName(SyntaxKind kind)
    {
        var name = kind.ToString();
        var result = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (index > 0 && char.IsUpper(character))
            {
                result.Append('-');
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    private static string RequiredContentHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    private static void ValidateMaxItems(int? maxItems)
    {
        if (maxItems < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxItems),
                maxItems,
                "The maximum outline item count cannot be negative.");
        }
    }

    private sealed class SignatureRewriter : CSharpSyntaxRewriter
    {
        public static SignatureRewriter Instance { get; } = new();

        public override SyntaxNode? VisitNamespaceDeclaration(
            NamespaceDeclarationSyntax node) =>
            base.VisitNamespaceDeclaration(
                node.WithExterns([]).WithUsings([]).WithMembers([]));

        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(
            FileScopedNamespaceDeclarationSyntax node) =>
            base.VisitFileScopedNamespaceDeclaration(
                node.WithExterns([]).WithUsings([]).WithMembers([]));

        public override SyntaxNode? VisitClassDeclaration(
            ClassDeclarationSyntax node) =>
            base.VisitClassDeclaration(
                node.WithAttributeLists([]).WithMembers([]));

        public override SyntaxNode? VisitStructDeclaration(
            StructDeclarationSyntax node) =>
            base.VisitStructDeclaration(
                node.WithAttributeLists([]).WithMembers([]));

        public override SyntaxNode? VisitInterfaceDeclaration(
            InterfaceDeclarationSyntax node) =>
            base.VisitInterfaceDeclaration(
                node.WithAttributeLists([]).WithMembers([]));

        public override SyntaxNode? VisitRecordDeclaration(
            RecordDeclarationSyntax node) =>
            base.VisitRecordDeclaration(
                node.WithAttributeLists([]).WithMembers([]));

        public override SyntaxNode? VisitPrimaryConstructorBaseType(
            PrimaryConstructorBaseTypeSyntax node) =>
            SyntaxFactory.SimpleBaseType(node.Type).WithTriviaFrom(node);

        public override SyntaxNode? VisitEnumDeclaration(
            EnumDeclarationSyntax node) =>
            base.VisitEnumDeclaration(
                node.WithAttributeLists([]).WithMembers([]));

        public override SyntaxNode? VisitDelegateDeclaration(
            DelegateDeclarationSyntax node) =>
            base.VisitDelegateDeclaration(node.WithAttributeLists([]));

        public override SyntaxNode? VisitMethodDeclaration(
            MethodDeclarationSyntax node) =>
            base.VisitMethodDeclaration(WithoutBody(
                node.WithAttributeLists([])));

        public override SyntaxNode? VisitConstructorDeclaration(
            ConstructorDeclarationSyntax node) =>
            base.VisitConstructorDeclaration(WithoutBody(
                node.WithAttributeLists([])));

        public override SyntaxNode? VisitDestructorDeclaration(
            DestructorDeclarationSyntax node) =>
            base.VisitDestructorDeclaration(WithoutBody(
                node.WithAttributeLists([])));

        public override SyntaxNode? VisitOperatorDeclaration(
            OperatorDeclarationSyntax node) =>
            base.VisitOperatorDeclaration(WithoutBody(
                node.WithAttributeLists([])));

        public override SyntaxNode? VisitConversionOperatorDeclaration(
            ConversionOperatorDeclarationSyntax node) =>
            base.VisitConversionOperatorDeclaration(WithoutBody(
                node.WithAttributeLists([])));

        public override SyntaxNode? VisitPropertyDeclaration(
            PropertyDeclarationSyntax node) =>
            base.VisitPropertyDeclaration(WithoutBody(
                node.WithAttributeLists([])));

        public override SyntaxNode? VisitIndexerDeclaration(
            IndexerDeclarationSyntax node) =>
            base.VisitIndexerDeclaration(WithoutBody(
                node.WithAttributeLists([])));

        public override SyntaxNode? VisitEventDeclaration(
            EventDeclarationSyntax node) =>
            base.VisitEventDeclaration(node.WithAttributeLists([]));

        public override SyntaxNode? VisitFieldDeclaration(
            FieldDeclarationSyntax node) =>
            base.VisitFieldDeclaration(
                node.WithAttributeLists([]).WithDeclaration(
                    WithoutInitializers(node.Declaration)));

        public override SyntaxNode? VisitEventFieldDeclaration(
            EventFieldDeclarationSyntax node) =>
            base.VisitEventFieldDeclaration(
                node.WithAttributeLists([]).WithDeclaration(
                    WithoutInitializers(node.Declaration)));

        public override SyntaxNode? VisitEnumMemberDeclaration(
            EnumMemberDeclarationSyntax node) =>
            base.VisitEnumMemberDeclaration(
                node.WithAttributeLists([]).WithEqualsValue(null));

        public override SyntaxNode? VisitAccessorDeclaration(
            AccessorDeclarationSyntax node) =>
            base.VisitAccessorDeclaration(WithoutBody(node));

        public override SyntaxNode? VisitLocalFunctionStatement(
            LocalFunctionStatementSyntax node) =>
            base.VisitLocalFunctionStatement(WithoutBody(
                node.WithAttributeLists([])));

        private static MethodDeclarationSyntax WithoutBody(
            MethodDeclarationSyntax node) =>
            node.WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        private static ConstructorDeclarationSyntax WithoutBody(
            ConstructorDeclarationSyntax node) =>
            node.WithBody(null)
                .WithExpressionBody(null)
                .WithInitializer(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        private static PropertyDeclarationSyntax WithoutBody(
            PropertyDeclarationSyntax node)
        {
            var accessors = node.AccessorList
                ?? GetterAccessorList(node.ExpressionBody is not null);
            return node.WithExpressionBody(null)
                .WithInitializer(null)
                .WithAccessorList(accessors)
                .WithSemicolonToken(default);
        }

        private static IndexerDeclarationSyntax WithoutBody(
            IndexerDeclarationSyntax node)
        {
            var accessors = node.AccessorList
                ?? GetterAccessorList(node.ExpressionBody is not null);
            return node.WithExpressionBody(null)
                .WithAccessorList(accessors)
                .WithSemicolonToken(default);
        }

        private static AccessorListSyntax? GetterAccessorList(
            bool expressionBodied) =>
            expressionBodied
                ? SyntaxFactory.AccessorList(
                    SyntaxFactory.SingletonList(
                        SyntaxFactory.AccessorDeclaration(
                                SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(
                                SyntaxFactory.Token(SyntaxKind.SemicolonToken))))
                : null;

        private static DestructorDeclarationSyntax WithoutBody(
            DestructorDeclarationSyntax node) =>
            node.WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        private static OperatorDeclarationSyntax WithoutBody(
            OperatorDeclarationSyntax node) =>
            node.WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        private static ConversionOperatorDeclarationSyntax WithoutBody(
            ConversionOperatorDeclarationSyntax node) =>
            node.WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        private static AccessorDeclarationSyntax WithoutBody(
            AccessorDeclarationSyntax node) =>
            node.WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        private static LocalFunctionStatementSyntax WithoutBody(
            LocalFunctionStatementSyntax node) =>
            node.WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        private static VariableDeclarationSyntax WithoutInitializers(
            VariableDeclarationSyntax declaration) =>
            declaration.WithVariables(
                SyntaxFactory.SeparatedList(declaration.Variables.Select(
                    static variable => variable.WithInitializer(null))));
    }
}
