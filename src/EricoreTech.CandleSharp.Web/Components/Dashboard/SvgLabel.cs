using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace EricoreTech.CandleSharp.Web.Components.Dashboard
{
    /// <summary>
    /// An SVG &lt;text&gt; element with attributes, built directly through
    /// RenderTreeBuilder. Razor's own markup parser reserves the literal tag
    /// name "text" as a non-rendering pseudo-element and refuses any attributes
    /// on it (RZ1023), so a chart's axis labels can't be written as plain
    /// &lt;text x="..." y="..."&gt; in a .razor file — this sidesteps that by
    /// not going through the markup parser at all.
    /// </summary>
    public sealed class SvgLabel : ComponentBase
    {
        [Parameter] public double X { get; set; }
        [Parameter] public double Y { get; set; }
        [Parameter] public string? Anchor { get; set; }
        [Parameter] public string CssClass { get; set; } = "axis-label";
        [Parameter] public string Text { get; set; } = "";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "text");
            builder.AddAttribute(1, "x", X.ToString("0.#", CultureInfo.InvariantCulture));
            builder.AddAttribute(2, "y", Y.ToString("0.#", CultureInfo.InvariantCulture));
            builder.AddAttribute(3, "class", CssClass);
            if (Anchor is not null) builder.AddAttribute(4, "text-anchor", Anchor);
            builder.AddContent(5, Text);
            builder.CloseElement();
        }
    }
}
