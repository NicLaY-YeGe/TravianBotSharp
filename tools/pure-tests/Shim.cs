global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using HtmlAgilityPack;
global using MainCore.Entities;
global using MainCore.Parsers;
global using Xunit;
global using Shouldly;

namespace StronglyTypedIds
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class StronglyTypedIdAttribute : Attribute { }
}

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)] public class FactAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class TheoryAttribute : FactAttribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class InlineDataAttribute : Attribute
    {
        public object?[] Data { get; }
        public InlineDataAttribute(params object?[] data) { Data = data; }
    }
}

namespace Shouldly
{
    public class ShouldAssertException : Exception { public ShouldAssertException(string m) : base(m) { } }

    public static class ShouldlyShim
    {
        static string Fmt(object? o) => o is System.Collections.IEnumerable e && o is not string
            ? "[" + string.Join(", ", e.Cast<object?>().Select(Fmt)) + "]" : (o?.ToString() ?? "null");

        public static void ShouldBe<T>(this T? actual, T? expected, string? customMessage = null)
        {
            if (!EqualityComparer<T?>.Default.Equals(actual, expected))
                throw new ShouldAssertException($"expected {Fmt(expected)} but was {Fmt(actual)}");
        }
        public static void ShouldBe<T>(this IEnumerable<T>? actual, IEnumerable<T>? expected, bool ignoreOrder = false, string? customMessage = null)
        {
            var a = actual!.ToList(); var e = expected!.ToList();
            if (ignoreOrder) { a = a.OrderBy(x => x?.GetHashCode()).ToList(); e = e.OrderBy(x => x?.GetHashCode()).ToList(); }
            if (!a.SequenceEqual(e)) throw new ShouldAssertException($"expected {Fmt(e)} but was {Fmt(a)}");
        }
        public static void ShouldBeTrue(this bool actual) { if (!actual) throw new ShouldAssertException("expected true"); }
        public static void ShouldBeFalse(this bool actual) { if (actual) throw new ShouldAssertException("expected false"); }
        public static void ShouldBeNull<T>(this T? actual) { if (actual is not null) throw new ShouldAssertException($"expected null but was {Fmt(actual)}"); }
        public static void ShouldBeNull<T>(this T? actual) where T : struct { if (actual.HasValue) throw new ShouldAssertException($"expected null but was {actual}"); }
        public static T ShouldNotBeNull<T>([System.Diagnostics.CodeAnalysis.NotNull] this T? actual) { if (actual is null) throw new ShouldAssertException("expected not null"); return actual; }
        public static void ShouldBeEmpty<T>(this IEnumerable<T> actual) { if (actual.Any()) throw new ShouldAssertException("expected empty"); }
        public static void ShouldAllBe<T>(this IEnumerable<T> actual, System.Linq.Expressions.Expression<Func<T, bool>> f)
        { var c = f.Compile(); if (!actual.All(c)) throw new ShouldAssertException("not all match"); }
        public static void ShouldBeSameAs(this object? actual, object? expected) { if (!ReferenceEquals(actual, expected)) throw new ShouldAssertException("expected same instance"); }
    }
}
