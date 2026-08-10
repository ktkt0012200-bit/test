using System;

namespace SkullDive.Tests
{
    public static class Program
    {
        public static int Main()
        {
            Console.WriteLine("SkullDive rule engine tests");
            Console.WriteLine();

            EngineTests.Register();
            RedactionTests.Register();
            OddsTests.Register();
            AiTests.Register();
            SoakTests.Register();
            WireTests.Register();
            CodecTests.Register();

            return T.Run();
        }
    }
}
