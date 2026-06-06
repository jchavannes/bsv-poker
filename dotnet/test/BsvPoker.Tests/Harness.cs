namespace BsvPoker.Tests;

/// <summary>Tiny dependency-free assertion + runner harness (no external test framework).</summary>
public static class T
{
    private static int _pass, _fail;

    public static void Run(string name, Action f)
    {
        try { f(); _pass++; Console.WriteLine($"  PASS  {name}"); }
        catch (Exception e) { _fail++; Console.WriteLine($"  FAIL  {name}: {e.Message}"); }
    }

    public static void Eq<TV>(TV actual, TV expected, string msg = "")
    {
        if (!Equals(actual, expected)) throw new Exception($"expected [{expected}], got [{actual}] {msg}");
    }
    public static void True(bool c, string msg = "") { if (!c) throw new Exception($"expected true: {msg}"); }
    public static void False(bool c, string msg = "") { if (c) throw new Exception($"expected false: {msg}"); }
    public static void Throws(Action f, string msg = "")
    {
        try { f(); } catch { return; }
        throw new Exception($"expected an exception: {msg}");
    }

    public static int Summary()
    {
        Console.WriteLine($"\n==== {_pass} passed, {_fail} failed ====");
        return _fail == 0 ? 0 : 1;
    }

    public static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
    public static byte[] Bytes(string hex) => Convert.FromHexString(hex);
    public static byte[] Seed(byte v) { var s = new byte[32]; s[31] = v; return s; }
}
