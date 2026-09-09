using System.Text;
using System.Text.RegularExpressions;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Options;

namespace Esi.RAG.Infrastructure;

/// <summary>
/// Extracts language-aware segments (and, where possible, structured symbols) from a single file.
/// Falls back to whole-file extraction whenever structured parsing fails or yields nothing, so
/// ingestion never silently drops content.
/// </summary>
public sealed class CompositeSourceExtractor : ISourceExtractor
{
    private static readonly Regex SqlInterestingStatement =
        new(@"^\s*(create|alter|drop|select|insert|update|delete|merge|with)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SqlObjectNameRegex = new(
        @"^\s*(create|alter)\s+(or\s+alter\s+)?(table|view|procedure|proc|function)\s+(\[?[\w\.\[\]]+\]?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<ExtractionResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(filePath);
        if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractCSharpAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        if (ext.Equals(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractSqlAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase) || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractMarkdownAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        return await ExtractGenericAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExtractionResult> ExtractGenericAsync(string filePath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var lineCount = text.Length == 0 ? 1 : text.Replace("\r\n", "\n").Split('\n').Length;
        var ext = Path.GetExtension(filePath).TrimStart('.');
        var language = FileDiscoveryService.LanguageFromExtension(ext);
        return new ExtractionResult(filePath, language, [new SourceSegment(filePath, language, "document", text, 1, lineCount)]);
    }

    private static async Task<ExtractionResult> ExtractMarkdownAsync(string filePath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var segments = new List<SourceSegment>();

        var currentStart = 1;
        var currentTitle = "markdown:section:intro";
        var builder = new StringBuilder();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var isHeading = line.TrimStart().StartsWith('#');
            if (isHeading && builder.Length > 0)
            {
                segments.Add(new SourceSegment(filePath, "markdown", currentTitle, builder.ToString().TrimEnd(), currentStart, index));
                builder.Clear();
                currentStart = index + 1;
            }

            if (isHeading)
            {
                var headingText = line.Trim().TrimStart('#').Trim();
                currentTitle = string.IsNullOrWhiteSpace(headingText)
                    ? "markdown:section:untitled"
                    : $"markdown:section:{headingText}";
            }

            builder.AppendLine(line);
        }

        if (builder.Length > 0)
        {
            segments.Add(new SourceSegment(filePath, "markdown", currentTitle, builder.ToString().TrimEnd(), currentStart, lines.Length));
        }

        if (segments.Count == 0)
        {
            segments.Add(new SourceSegment(filePath, "markdown", "markdown:fallback", text, 1, lines.Length));
        }

        return new ExtractionResult(filePath, "markdown", segments);
    }

    private static async Task<ExtractionResult> ExtractCSharpAsync(string filePath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var sourceText = await tree.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var segments = new List<SourceSegment>();
        var symbols = new List<CodeSymbol>();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ClassDeclarationSyntax classNode:
                    AddTypeSegment("class", classNode, classNode.Identifier.Text);
                    break;
                case RecordDeclarationSyntax recordNode:
                    AddTypeSegment("record", recordNode, recordNode.Identifier.Text);
                    break;
                case StructDeclarationSyntax structNode:
                    AddTypeSegment("struct", structNode, structNode.Identifier.Text);
                    break;
                case InterfaceDeclarationSyntax interfaceNode:
                    AddTypeSegment("interface", interfaceNode, interfaceNode.Identifier.Text);
                    break;
                case MethodDeclarationSyntax methodNode:
                    AddMemberSegment("method", methodNode, methodNode.Identifier.Text, methodNode.ParameterList.ToString());
                    break;
                case InvocationExpressionSyntax invocationNode when IsEfMappingExpression(invocationNode):
                    AddEfMappingSegment(invocationNode);
                    break;
            }
        }

        var hasFatalErrors = tree.GetDiagnostics(cancellationToken).Any(static d => d.Severity == DiagnosticSeverity.Error);
        if (segments.Count == 0 || hasFatalErrors)
        {
            segments = [new SourceSegment(filePath, "csharp", "syntax-fallback", text, 1, sourceText.Lines.Count)];
            symbols.Clear();
        }

        return new ExtractionResult(filePath, "csharp", segments, symbols);

        void AddTypeSegment(string kind, SyntaxNode node, string name)
        {
            var (span, lineSpan, content) = SpanOf(node);
            var ns = NamespaceOf(node);
            var symbol = ns is null ? $"{kind}:{name}" : $"{kind}:{ns}.{name}";
            segments.Add(new SourceSegment(filePath, "csharp", symbol, content, lineSpan.Start.Line + 1, lineSpan.End.Line + 1));
            symbols.Add(new CodeSymbol(kind, name, ns, null, filePath, lineSpan.Start.Line + 1, lineSpan.End.Line + 1, name));
        }

        void AddMemberSegment(string kind, SyntaxNode node, string name, string signatureSuffix)
        {
            var (span, lineSpan, content) = SpanOf(node);
            var ns = NamespaceOf(node);
            var containingType = ContainingTypeOf(node);
            var qualifiedPrefix = string.Join('.', new[] { ns, containingType }.Where(part => !string.IsNullOrEmpty(part)));
            var symbol = string.IsNullOrEmpty(qualifiedPrefix) ? $"{kind}:{name}" : $"{kind}:{qualifiedPrefix}.{name}";
            segments.Add(new SourceSegment(filePath, "csharp", symbol, content, lineSpan.Start.Line + 1, lineSpan.End.Line + 1));
            symbols.Add(new CodeSymbol(kind, name, ns, containingType, filePath, lineSpan.Start.Line + 1, lineSpan.End.Line + 1, $"{name}{signatureSuffix}"));
        }

        void AddEfMappingSegment(InvocationExpressionSyntax invocationNode)
        {
            var statement = invocationNode.AncestorsAndSelf().OfType<ExpressionStatementSyntax>().FirstOrDefault();
            SyntaxNode node = statement is null ? invocationNode : statement;
            var (_, lineSpan, content) = SpanOf(node);
            var expression = invocationNode.Expression.ToString();
            var symbol = $"ef-mapping:{expression}";
            segments.Add(new SourceSegment(
                filePath,
                "csharp",
                symbol,
                content,
                lineSpan.Start.Line + 1,
                lineSpan.End.Line + 1));
        }

        static bool IsEfMappingExpression(InvocationExpressionSyntax invocationNode)
        {
            var expression = invocationNode.Expression.ToString();
            return expression.Contains(".HasColumnName", StringComparison.Ordinal) &&
                   expression.Contains(".Property", StringComparison.Ordinal);
        }

        (Microsoft.CodeAnalysis.Text.TextSpan Span, Microsoft.CodeAnalysis.Text.LinePositionSpan LineSpan, string Content) SpanOf(SyntaxNode node)
        {
            var span = node.Span;
            var lineSpan = sourceText.Lines.GetLinePositionSpan(span);
            return (span, lineSpan, sourceText.ToString(span));
        }

        static string? NamespaceOf(SyntaxNode node) => node.Ancestors()
            .Select(static ancestor => ancestor switch
            {
                NamespaceDeclarationSyntax namespaceNode => namespaceNode.Name.ToString(),
                FileScopedNamespaceDeclarationSyntax fileScoped => fileScoped.Name.ToString(),
                _ => null,
            })
            .FirstOrDefault(static value => value is not null);

        static string? ContainingTypeOf(SyntaxNode node) => node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Select(static type => type.Identifier.Text)
            .FirstOrDefault();
    }

    private static async Task<ExtractionResult> ExtractSqlAsync(string filePath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var allLines = text.Replace("\r\n", "\n").Split('\n');
        var statements = text.Split(';');
        var segments = new List<SourceSegment>();
        var sqlObjects = new List<SqlObject>();
        var statementOffset = 0;

        foreach (var statement in statements)
        {
            var trimmed = statement.Trim();
            var sqlContent = RemoveLeadingComments(trimmed);
            if (string.IsNullOrWhiteSpace(sqlContent) || !SqlInterestingStatement.IsMatch(sqlContent))
            {
                statementOffset += statement.Length + 1;
                continue;
            }

            var contentOffset = statementOffset + statement.IndexOf(sqlContent, StringComparison.Ordinal);
            var startLine = 1 + text[..Math.Max(0, contentOffset)].Count(static character => character == '\n');
            var endLine = startLine + sqlContent.Count(static character => character == '\n');
            statementOffset += statement.Length + 1;

            var keyword = sqlContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            var statementText = $"{sqlContent};";
            segments.Add(new SourceSegment(filePath, "sql", $"sql:{keyword}", statementText, startLine, endLine));

            var objectMatch = SqlObjectNameRegex.Match(sqlContent);
            if (objectMatch.Success)
            {
                var objectType = objectMatch.Groups[3].Value.ToLowerInvariant() switch
                {
                    "proc" => "procedure",
                    var value => value,
                };
                var rawName = objectMatch.Groups[4].Value.Trim('[', ']');
                var parts = rawName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var schema = parts.Length > 1 ? parts[^2].Trim('[', ']') : null;
                var name = parts[^1].Trim('[', ']');
                var excerpt = statementText.Length > 400 ? $"{statementText[..400]}..." : statementText;
                sqlObjects.Add(new SqlObject(objectType, name, schema, filePath, startLine, endLine, excerpt));
            }
        }

        if (segments.Count == 0)
        {
            segments.Add(new SourceSegment(filePath, "sql", "sql-fallback", text, 1, allLines.Length));
        }

        return new ExtractionResult(filePath, "sql", segments, null, sqlObjects);

        static string RemoveLeadingComments(string value)
        {
            var remaining = value.TrimStart();
            while (remaining.StartsWith("--", StringComparison.Ordinal))
            {
                var newline = remaining.IndexOf('\n');
                if (newline < 0)
                {
                    return string.Empty;
                }

                remaining = remaining[(newline + 1)..].TrimStart();
            }

            return remaining;
        }
    }
}

/// <summary>Splits segments larger than the configured line budget into overlapping chunks.</summary>
public sealed class LinePreservingChunker(IOptions<RagOptions> options) : IChunker
{
    private readonly int _maxLines = Math.Max(10, options.Value.ChunkMaxLines);
    private readonly int _overlapLines = Math.Clamp(options.Value.ChunkOverlapLines, 0, Math.Max(10, options.Value.ChunkMaxLines) - 1);

    public IReadOnlyList<SourceSegment> Chunk(ExtractionResult extractionResult)
    {
        var chunks = new List<SourceSegment>();
        foreach (var segment in extractionResult.Segments)
        {
            var lines = segment.Content.Replace("\r\n", "\n").Split('\n');
            if (lines.Length <= _maxLines)
            {
                chunks.Add(segment);
                continue;
            }

            var index = 0;
            while (index < lines.Length)
            {
                var take = Math.Min(_maxLines, lines.Length - index);
                var chunkLines = lines.Skip(index).Take(take);
                var startLine = segment.StartLine + index;
                var endLine = startLine + take - 1;
                chunks.Add(segment with
                {
                    Content = string.Join(Environment.NewLine, chunkLines),
                    StartLine = startLine,
                    EndLine = endLine
                });

                if (index + take >= lines.Length)
                {
                    break;
                }

                index += Math.Max(1, take - _overlapLines);
            }
        }

        return chunks;
    }
}
