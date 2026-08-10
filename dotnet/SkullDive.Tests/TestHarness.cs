using System;
using System.Collections.Generic;

namespace SkullDive.Tests
{
    /// <summary>依存パッケージなしの最小テストランナー。</summary>
    public static class T
    {
        private sealed class Case
        {
            public string Name;
            public Action Body;
        }

        private static readonly List<Case> Cases = new List<Case>();
        private static string _currentSection = "";

        public static void Section(string name)
        {
            _currentSection = name;
        }

        public static void Test(string name, Action body)
        {
            Cases.Add(new Case { Name = (_currentSection.Length > 0 ? _currentSection + " / " : "") + name, Body = body });
        }

        public static int Run()
        {
            int passed = 0;
            var failures = new List<string>();

            foreach (var testCase in Cases)
            {
                try
                {
                    testCase.Body();
                    passed++;
                    Console.WriteLine("  PASS  " + testCase.Name);
                }
                catch (Exception ex)
                {
                    failures.Add(testCase.Name + "\n        " + ex.Message);
                    Console.WriteLine("  FAIL  " + testCase.Name);
                    Console.WriteLine("        " + ex.Message);
                    if (!(ex is AssertException))
                    {
                        Console.WriteLine("        " + ex.GetType().Name);
                        Console.WriteLine(Indent(ex.StackTrace, "        "));
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failures.Count + " failed, " + Cases.Count + " total");
            return failures.Count == 0 ? 0 : 1;
        }

        private static string Indent(string text, string prefix)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return prefix + text.Replace("\n", "\n" + prefix);
        }

        // ------------------------------------------------------------ assertions

        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertException("expected true: " + message);
        }

        public static void False(bool condition, string message)
        {
            if (condition) throw new AssertException("expected false: " + message);
        }

        public static void Eq<TValue>(TValue expected, TValue actual, string message)
        {
            if (!EqualityComparer<TValue>.Default.Equals(expected, actual))
            {
                throw new AssertException(message + ": expected <" + expected + "> but was <" + actual + ">");
            }
        }

        public static void Near(double expected, double actual, double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new AssertException(message + ": expected <" + expected + "> but was <" + actual + ">");
            }
        }

        public static void Throws<TException>(Action body, string message) where TException : Exception
        {
            try
            {
                body();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertException(message + ": expected " + typeof(TException).Name + " but got " + ex.GetType().Name);
            }
            throw new AssertException(message + ": expected " + typeof(TException).Name + " but nothing was thrown");
        }
    }

    public sealed class AssertException : Exception
    {
        public AssertException(string message) : base(message) { }
    }
}
