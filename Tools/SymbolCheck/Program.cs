using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoadRage.Tools
{
    /// Catches calls to methods that are not defined anywhere in the project.
    ///
    /// WHY THIS EXISTS
    ///
    /// The obvious check - compile the scripts with csc and read the errors - cannot do
    /// this job, and quietly did not. Roslyn skips method-body binding entirely when a
    /// compilation has any declaration-level error, and compiling Unity scripts without
    /// Unity produces hundreds of CS0246 for every UnityEngine type. Verified directly:
    ///
    ///     class KnownBase { }
    ///     class C : KnownBase { void B() { DoesNotExist(); } }   -> CS0103, caught
    ///
    ///     class C : SomeUnknownBase { void B() { DoesNotExist(); } }
    ///                                                            -> silence
    ///
    /// One unresolved type anywhere is enough to switch off body binding for the whole
    /// compilation, so a "zero new errors" reading from that harness meant nothing about
    /// method bodies. Commit ffaeb73 shipped calling GetBracketKey() and GetExposureKey(),
    /// neither of which existed, and the harness reported its usual clean baseline.
    ///
    /// HOW THIS ONE WORKS
    ///
    /// It never asks the compiler to resolve UnityEngine. It parses the project's own
    /// sources, collects every member name the project declares, and then finds every
    /// unqualified call - Foo(), not thing.Foo() - whose name is declared nowhere in the
    /// project. Those are either Unity API calls inherited from MonoBehaviour, or typos.
    ///
    /// Telling those two apart is what baseline.txt is for. The project compiles in Unity
    /// today, so everything unresolved today is by definition a real Unity API. Anything
    /// that appears later and is not in that list is a call to something that does not
    /// exist. That is the entire check, and it is the one that would have caught ffaeb73.
    ///
    /// WHAT IT DOES NOT DO
    ///
    /// Names only. It does not check argument counts, argument types, or return types, and
    /// it deliberately resolves a name declared on any type in the project rather than
    /// insisting it is on the calling type - calling a real method on the wrong object
    /// passes here. It is a floor, not a substitute for compiling in Unity.
    public static class Program
    {
        private static readonly string[] SourceRoots = { "Assets/Scripts", "Assets/Editor" };

        public static int Main(string[] args)
        {
            var repo = args.FirstOrDefault(a => !a.StartsWith("-")) ?? ".";
            if (args.Contains("--selftest")) return SelfTest(repo);

            var update = args.Contains("--update-baseline");
            var baselinePath = Path.Combine(repo, "Tools/SymbolCheck/baseline.txt");

            if (!Analyze(repo, out var files, out var calls, out var declaredCount)) return 2;
            var found = calls.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

            if (update)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath));
                File.WriteAllLines(baselinePath, new[]
                {
                    "# Names called unqualified in this project that the project does not declare.",
                    "# Everything here is a Unity API reached through MonoBehaviour or a using.",
                    "# Regenerate with: dotnet run --project Tools/SymbolCheck -- . --update-baseline",
                    "# A name appearing at check time that is NOT in this list is a call to",
                    "# something that does not exist. See Program.cs for why csc cannot do this.",
                }.Concat(found));
                Console.WriteLine($"SYMBOLCHECK baseline written: {found.Count} external names, {files.Count} files");
                return 0;
            }

            if (!File.Exists(baselinePath))
            {
                Console.Error.WriteLine($"SYMBOLCHECK no baseline at {baselinePath}; run with --update-baseline");
                return 2;
            }

            var baseline = File.ReadAllLines(baselinePath)
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToHashSet(StringComparer.Ordinal);

            var added = found.Where(n => !baseline.Contains(n)).ToList();

            Console.WriteLine($"SYMBOLCHECK {files.Count} files, {declaredCount} declared names, " +
                              $"{found.Count} external calls, {added.Count} undeclared");

            if (added.Count == 0) return 0;

            foreach (var name in added)
            {
                Console.WriteLine($"SYMBOLCHECK UNDEFINED {name}");
                foreach (var site in calls[name].Take(5)) Console.WriteLine($"    {site}");
            }
            Console.WriteLine($"SYMBOLCHECK FAILED: {added.Count} name(s) called but declared nowhere.");
            return 1;
        }

        /// Parses the project's own sources and returns the unqualified calls it cannot
        /// account for. Shared by the real check and the self test so the two can never
        /// drift into testing different code.
        private static bool Analyze(string repo, out List<string> files,
            out Dictionary<string, List<string>> calls, out int declaredCount)
        {
            files = SourceRoots
                .Select(r => Path.Combine(repo, r))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            calls = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            declaredCount = 0;

            if (files.Count == 0)
            {
                Console.Error.WriteLine($"SYMBOLCHECK no sources found under " +
                                        $"{string.Join(", ", SourceRoots)} in {repo}");
                return false;
            }

            var trees = files
                .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
                .ToList();
            var declared = CollectDeclaredNames(trees);
            declaredCount = declared.Count;
            calls = CollectUnresolvedCalls(trees, declared);
            return true;
        }

        /// Proves the checker still detects a call to something that does not exist.
        ///
        /// This exists because the harness it replaced failed silently for weeks: it kept
        /// reporting a healthy-looking error count while being structurally incapable of
        /// seeing a missing method. A check that can quietly stop checking is worse than
        /// no check, so this one is asked to catch a planted bug on every run.
        ///
        /// The fixture also carries the constructs most likely to produce a false alarm -
        /// a local function used above its declaration, delegate-typed field, local and
        /// parameter invocations, a call into another file, nameof(), a static on the
        /// calling class, and a real Unity API. Exactly one name in it is undefined.
        private static int SelfTest(string repo)
        {
            const string expected = "ThisOneIsGenuinelyMissing";
            var fixture = Path.Combine(repo, "Tools/SymbolCheck/selftest");

            if (!Analyze(fixture, out var files, out var calls, out _)) return 2;

            var baseline = File.ReadAllLines(Path.Combine(fixture, "Tools/SymbolCheck/baseline.txt"))
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToHashSet(StringComparer.Ordinal);
            var flagged = calls.Keys.Where(n => !baseline.Contains(n))
                .OrderBy(n => n, StringComparer.Ordinal).ToList();

            if (flagged.Count == 1 && flagged[0] == expected)
            {
                Console.WriteLine($"SYMBOLCHECK selftest OK: caught {expected} " +
                                  $"and nothing else across {files.Count} fixture files");
                return 0;
            }

            Console.Error.WriteLine($"SYMBOLCHECK SELFTEST FAILED: expected exactly [{expected}], " +
                                    $"got [{string.Join(", ", flagged)}]");
            Console.Error.WriteLine("The checker is no longer catching undefined names, or is " +
                                    "flagging something it should not. Do not trust its green runs.");
            return 1;
        }

        /// Every name the project declares that a bare call could legitimately land on.
        /// Deliberately flat and generous: a name declared on any type counts. Narrowing
        /// it to the calling type's own hierarchy is impossible here anyway, because that
        /// hierarchy runs into MonoBehaviour, which this tool never resolves.
        private static HashSet<string> CollectDeclaredNames(IEnumerable<SyntaxTree> trees)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tree in trees)
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case MethodDeclarationSyntax m: names.Add(m.Identifier.ValueText); break;
                    case LocalFunctionStatementSyntax l: names.Add(l.Identifier.ValueText); break;
                    case PropertyDeclarationSyntax p: names.Add(p.Identifier.ValueText); break;
                    case ConstructorDeclarationSyntax c: names.Add(c.Identifier.ValueText); break;
                    case DelegateDeclarationSyntax d: names.Add(d.Identifier.ValueText); break;
                    case TypeDeclarationSyntax t: names.Add(t.Identifier.ValueText); break;
                    case EnumDeclarationSyntax e: names.Add(e.Identifier.ValueText); break;
                    case EventDeclarationSyntax ev: names.Add(ev.Identifier.ValueText); break;
                    case FieldDeclarationSyntax f:
                        foreach (var v in f.Declaration.Variables) names.Add(v.Identifier.ValueText);
                        break;
                    case EventFieldDeclarationSyntax ef:
                        foreach (var v in ef.Declaration.Variables) names.Add(v.Identifier.ValueText);
                        break;
                }
            }
            return names;
        }

        /// Unqualified calls whose name the project never declares.
        ///
        /// Only bare Foo() is considered. thing.Foo() is skipped on purpose: the receiver
        /// is usually a Unity type this tool cannot see, so every such call would be
        /// unresolvable and the signal would drown. Bare calls are the ones that land on
        /// the enclosing type, which is exactly where a deleted or misspelled method bites.
        private static Dictionary<string, List<string>> CollectUnresolvedCalls(
            IEnumerable<SyntaxTree> trees, HashSet<string> declared)
        {
            var unresolved = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var tree in trees)
            {
                var text = tree.GetText();
                foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (call.Expression is not IdentifierNameSyntax id) continue;
                    var name = id.Identifier.ValueText;
                    if (declared.Contains(name)) continue;
                    // nameof() and friends are contextual keywords, not calls.
                    if (name is "nameof" or "sizeof" or "typeof" or "default") continue;
                    if (LocallyBound(call, name)) continue;

                    var line = text.Lines.GetLineFromPosition(id.SpanStart).LineNumber + 1;
                    if (!unresolved.TryGetValue(name, out var sites))
                        unresolved[name] = sites = new List<string>();
                    sites.Add($"{tree.FilePath}:{line}");
                }
            }
            return unresolved;
        }

        /// Is this name a local, parameter, or foreach/catch variable in scope? Those are
        /// invoked when they hold a delegate, and they are declared inside the body rather
        /// than as a member, so the declared-name sweep does not see them.
        private static bool LocallyBound(SyntaxNode call, string name)
        {
            for (var node = call.Parent; node != null; node = node.Parent)
            {
                foreach (var child in node.DescendantNodes())
                {
                    switch (child)
                    {
                        case VariableDeclaratorSyntax v when v.Identifier.ValueText == name: return true;
                        case ParameterSyntax p when p.Identifier.ValueText == name: return true;
                        case ForEachStatementSyntax fe when fe.Identifier.ValueText == name: return true;
                        case SingleVariableDesignationSyntax sv when sv.Identifier.ValueText == name: return true;
                        case CatchDeclarationSyntax cd when cd.Identifier.ValueText == name: return true;
                    }
                }
                if (node is MemberDeclarationSyntax) break;
            }
            return false;
        }
    }
}
