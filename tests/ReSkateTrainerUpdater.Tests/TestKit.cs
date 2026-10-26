namespace ReSkateTrainerUpdater.Tests;

/// <summary>A tiny test harness: named checks, a pass/fail tally, and a section heading per group.</summary>
internal static class TestKit
{
    public static int Failures { get; private set; }
    public static int Total { get; private set; }

    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    public static void Check(string name, bool condition, string? detail = null)
    {
        Total++;
        if (!condition) Failures++;
        Console.WriteLine($"  {(condition ? "pass" : "FAIL")}  {name}{(!condition && detail is { Length: > 0 } ? "  -> " + detail : "")}");
    }

    public static void Skip(string name, string reason) => Console.WriteLine($"  skip  {name} ({reason})");
}
