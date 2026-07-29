using EricoreTech.CandleSharp.Domain;

using System.Reflection;
using System.Runtime.Loader;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>An indicator plus where it came from ("built-in" or "plugin:&lt;file&gt;").</summary>
    public sealed record RegisteredIndicator(ITechnicalIndicator Indicator, string Source);
}
