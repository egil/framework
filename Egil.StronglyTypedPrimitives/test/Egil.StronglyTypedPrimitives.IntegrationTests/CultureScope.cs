using System.Globalization;

namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// Switches <see cref="CultureInfo.CurrentCulture"/> for the lifetime of the scope so a test can
/// prove that output is culture invariant without leaving the culture changed for other tests.
/// </summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo previous;

    private CultureScope(CultureInfo culture)
    {
        previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
    }

    public static CultureScope Use(string cultureName) => new(new CultureInfo(cultureName));

    public void Dispose() => CultureInfo.CurrentCulture = previous;
}