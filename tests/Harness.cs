namespace PxeVice.Tests
{
    using System;

    public static class T
    {
        public static int Passed, Failed;

        public static void Check(bool ok, string what)
        {
            if (ok) Passed++;
            else
            {
                Failed++;
                Console.WriteLine("FAIL: " + what);
            }
        }

        public static void Equal(object expected, object actual, string what)
        {
            Check(Equals(expected, actual), what + ": expected " + (expected ?? "null") + ", got " + (actual ?? "null"));
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            ActivityLog.Enabled = false;
            EngineTests.Run();
            Console.WriteLine(T.Passed + " passed, " + T.Failed + " failed");
            return T.Failed == 0 ? 0 : 1;
        }
    }
}
