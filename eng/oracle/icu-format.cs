// The CLDR/ICU oracle. Formats (locale, skeleton, value) through ICU 78's unumf_* skeleton
// formatter — the same `::`-grammar our engine implements — by P/Invoking the dev-only ICU
// runtime that fetch-icu.cs downloads. Ground truth for parity tests; never shipped with the
// library. ICU 78 == CLDR 48, matching the pinned data in eng/cldr.
//
//   Single:  dotnet run eng/oracle/icu-format.cs -- <locale> <skeleton> <value>
//            dotnet run eng/oracle/icu-format.cs -- fr "currency/USD" 1234.56
//   Batch:   pipe tab-separated `locale<TAB>skeleton<TAB>value` lines on stdin; each emits the
//            same three columns plus a fourth `result` column. One process formats many cases.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ArchPillar.Oracle;

internal static class IcuFormatOracle
{
    private static void Main(string[] args)
    {
        // Faithful bytes out: Windows stdout otherwise defaults to the OEM code page and mangles € / NBSP.
        Console.OutputEncoding = Encoding.UTF8;

        // icuin78.dll depends on icuuc78.dll + icudt78.dll sitting beside it; put that dir on PATH so
        // the native loader resolves all three before the first P/Invoke.
        var icuDirectory = Path.Combine(ScriptDirectory(), "icu");
        Environment.SetEnvironmentVariable(
            "PATH", icuDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));

        if (args.Length >= 3)
        {
            Console.WriteLine(Format(args[0], args[1], args[2]));
            return;
        }

        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length < 3)
            {
                Console.WriteLine(line + "\tERROR:malformed");
                continue;
            }

            Console.WriteLine($"{columns[0]}\t{columns[1]}\t{columns[2]}\t{Format(columns[0], columns[1], columns[2])}");
        }
    }

    private static string Format(string locale, string skeleton, string value)
    {
        // The ICU skeleton API takes the stems without MessageFormat's leading "::".
        if (skeleton.StartsWith("::", StringComparison.Ordinal))
        {
            skeleton = skeleton[2..];
        }

        var status = 0;
        var formatter = Native.OpenForSkeletonAndLocale(skeleton, -1, locale, ref status);
        if (Failed(status))
        {
            return $"ERROR:open:{status}";
        }

        var result = Native.OpenResult(ref status);
        if (Failed(status))
        {
            Native.Close(formatter);
            return $"ERROR:result:{status}";
        }

        string output;
        Native.FormatDecimal(formatter, value, -1, result, ref status);
        if (Failed(status))
        {
            output = $"ERROR:format:{status}";
        }
        else
        {
            var buffer = new StringBuilder(256);
            var length = Native.ResultToString(result, buffer, buffer.Capacity, ref status);
            if (status == BufferOverflow)
            {
                status = 0;
                buffer = new StringBuilder(length + 1);
                Native.ResultToString(result, buffer, buffer.Capacity, ref status);
            }

            output = Failed(status) ? $"ERROR:tostring:{status}" : buffer.ToString();
        }

        Native.CloseResult(result);
        Native.Close(formatter);
        return output;
    }

    // UErrorCode: 0 == U_ZERO_ERROR, > 0 == failure, < 0 == warning (e.g. fallback-locale).
    private const int BufferOverflow = 15; // U_BUFFER_OVERFLOW_ERROR

    private static bool Failed(int status) => status > 0;

    private static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    // ICU 78 C API (unumberformatter.h). Symbol renaming appends the major version, so the exported
    // entry points carry a `_78` suffix. Lives in icuin78.dll (the i18n library).
    private static class Native
    {
        private const string Lib = "icuin78.dll";

        [DllImport(Lib, EntryPoint = "unumf_openForSkeletonAndLocale_78")]
        public static extern IntPtr OpenForSkeletonAndLocale(
            [MarshalAs(UnmanagedType.LPWStr)] string skeleton,
            int skeletonLength,
            [MarshalAs(UnmanagedType.LPStr)] string locale,
            ref int errorCode);

        [DllImport(Lib, EntryPoint = "unumf_openResult_78")]
        public static extern IntPtr OpenResult(ref int errorCode);

        [DllImport(Lib, EntryPoint = "unumf_formatDecimal_78")]
        public static extern void FormatDecimal(
            IntPtr formatter,
            [MarshalAs(UnmanagedType.LPStr)] string value,
            int valueLength,
            IntPtr result,
            ref int errorCode);

        [DllImport(Lib, EntryPoint = "unumf_resultToString_78", CharSet = CharSet.Unicode)]
        public static extern int ResultToString(IntPtr result, StringBuilder buffer, int capacity, ref int errorCode);

        [DllImport(Lib, EntryPoint = "unumf_closeResult_78")]
        public static extern void CloseResult(IntPtr result);

        [DllImport(Lib, EntryPoint = "unumf_close_78")]
        public static extern void Close(IntPtr formatter);
    }
}
