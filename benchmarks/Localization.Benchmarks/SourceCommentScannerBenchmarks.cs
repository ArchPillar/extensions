using System.Text;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using BenchmarkDotNet.Attributes;

namespace ArchPillar.Extensions.Localization.Benchmarks;

/// <summary>
/// Measures the build-time cost of recovering translation comments from source: a full syntax-only parse and scan
/// of a project's <c>.cs</c> files, scaled by file count. This runs once per extract (a post-build step), so the
/// number that matters is wall-clock over a realistic tree — it must stay a small fraction of a build, not grow
/// into it. Each generated file mixes translatable calls (some commented, some not) with ordinary filler.
/// </summary>
[MemoryDiagnoser]
public class SourceCommentScannerBenchmarks
{
    [Params(50, 250, 1000)]
    public int FileCount { get; set; }

    private string _root = null!;

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "aplscanbench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        for (var index = 0; index < FileCount; index++)
        {
            File.WriteAllText(Path.Combine(_root, $"Widget{index}.cs"), FileSource(index));
        }
    }

    [Benchmark]
    public object Scan() => SourceCommentScanner.Scan(_root);

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string FileSource(int index)
    {
        var builder = new StringBuilder();
        builder.Append("namespace Bench.Generated;\n\n");
        builder.Append($"public sealed class Widget{index}\n{{\n");
        builder.Append("    private readonly ILocalizer _l = null!;\n\n");
        builder.Append($"    public string Title() => _l.Translate(\"widget.{index}.title\", \"Widget {index}\" /* the card header */);\n");
        builder.Append($"    public string Subtitle() => _l.Translate(\"widget.{index}.subtitle\", \"A helpful widget\");\n");
        builder.Append($"    public string Cta() => _l.Translate(\"widget.{index}.cta\", \"Learn more\" /* keep it under 12 characters */);\n\n");
        builder.Append("    // Ordinary code with no translation, so the scan pays the parse cost of a real file.\n");
        builder.Append($"    public int Compute(int a, int b) => (a * b) + {index};\n");
        builder.Append("    public string Describe(int n) => n > 0 ? \"positive\" : \"non-positive\";\n");
        builder.Append("}\n");
        builder.Append("\ninternal interface ILocalizer { string Translate(string key, string @default); }\n");
        return builder.ToString();
    }
}
