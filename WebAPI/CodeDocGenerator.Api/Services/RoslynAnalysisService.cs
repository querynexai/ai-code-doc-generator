using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeDocGenerator.Api.Services;

public class RoslynAnalysisService
{
    /// <summary>
    /// Analyzes a C# project by parsing all .cs files in its directory.
    /// Accepts either a .csproj/.sln path (uses its containing folder) or a folder path directly.
    /// </summary>
    public (ProjectInfo Info, List<MemberInfo> Members) Analyze(string projectPath)
    {
        // Determine the folder to scan
        var folder = ResolveProjectFolder(projectPath);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Project folder not found: {folder}");

        // Find all .cs files (skip bin/obj)
        var csFiles = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}"))
            .ToList();

        var members = new List<MemberInfo>();
        var info = new ProjectInfo
        {
            ProjectName = Path.GetFileNameWithoutExtension(projectPath),
            AssemblyName = Path.GetFileNameWithoutExtension(projectPath),
            TargetFramework = "net10.0",
            TotalFiles = csFiles.Count
        };

        foreach (var file in csFiles)
        {
            try
            {
                var sourceText = File.ReadAllText(file);
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: file);
                var root = syntaxTree.GetRoot();

                // Walk all type declarations
                var typeDeclarations = root.DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>();

                foreach (var typeDecl in typeDeclarations)
                {
                    members.Add(new MemberInfo
                    {
                        MemberType = GetTypeKind(typeDecl),
                        MemberName = typeDecl.Identifier.Text,
                        FullyQualifiedName = typeDecl.Identifier.Text,
                        FilePath = file,
                        LineNumber = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        OriginalXml = GetXmlDoc(typeDecl)
                    });

                    if (typeDecl is not TypeDeclarationSyntax typeDeclSyntax) continue;

                    // Walk members inside the type
                    foreach (var member in typeDeclSyntax.Members)
                    {
                        members.Add(new MemberInfo
                        {
                            MemberType = GetMemberKind(member),
                            MemberName = GetMemberName(member),
                            FullyQualifiedName = $"{typeDecl.Identifier.Text}.{GetMemberName(member)}",
                            FilePath = file,
                            LineNumber = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            OriginalXml = GetXmlDoc(member)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Log and continue — one bad file shouldn't stop the whole analysis
                Console.WriteLine($"[Roslyn] Failed to parse {file}: {ex.Message}");
            }
        }

        info.TotalClasses = members.Count(m =>
            m.MemberType is "class" or "interface" or "record" or "struct" or "enum");
        info.TotalMethods = members.Count(m => m.MemberType == "method");
        info.TotalProperties = members.Count(m => m.MemberType == "property");
        info.DocumentedMembers = members.Count(m => !string.IsNullOrWhiteSpace(m.OriginalXml));
        info.UndocumentedMembers = members.Count - info.DocumentedMembers;

        return (info, members);
    }

    // ---------- Helpers ----------

    private static string ResolveProjectFolder(string path)
    {
        if (File.Exists(path))
            return Path.GetDirectoryName(path) ?? path;

        if (Directory.Exists(path))
            return path;

        return path;
    }

    private static string GetTypeKind(BaseTypeDeclarationSyntax syntax) => syntax switch
    {
        ClassDeclarationSyntax => "class",
        InterfaceDeclarationSyntax => "interface",
        RecordDeclarationSyntax => "record",
        StructDeclarationSyntax => "struct",
        EnumDeclarationSyntax => "enum",
        _ => "type"
    };

    private static string GetMemberKind(MemberDeclarationSyntax syntax) => syntax switch
    {
        MethodDeclarationSyntax => "method",
        PropertyDeclarationSyntax => "property",
        FieldDeclarationSyntax => "field",
        EventDeclarationSyntax => "event",
        ConstructorDeclarationSyntax => "constructor",
        _ => "member"
    };

    private static string GetMemberName(MemberDeclarationSyntax syntax) => syntax switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "field",
        _ => "member"
    };

    private static string? GetXmlDoc(SyntaxNode node)
    {
        var leadingTrivia = node.GetLeadingTrivia();
        var xmlTrivia = leadingTrivia
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();
        return xmlTrivia?.ToFullString();
    }

    // ---------- DTOs ----------

    public class ProjectInfo
    {
        public string ProjectName { get; set; } = string.Empty;
        public string? AssemblyName { get; set; }
        public string? TargetFramework { get; set; }
        public int TotalFiles { get; set; }
        public int TotalClasses { get; set; }
        public int TotalMethods { get; set; }
        public int TotalProperties { get; set; }
        public int DocumentedMembers { get; set; }
        public int UndocumentedMembers { get; set; }
    }

    public class MemberInfo
    {
        public string MemberType { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string? FullyQualifiedName { get; set; }
        public string? FilePath { get; set; }
        public int? LineNumber { get; set; }
        public string? OriginalXml { get; set; }
    }
}