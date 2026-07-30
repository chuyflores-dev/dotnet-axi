namespace FixtureApp;

public static class FixtureEntry
{
    public static string Value =>
        GeneratedMessage.Value + LinkedMessage.Value + Framework;

#if NET10_0_WINDOWS
    private const string Framework = ":net10-windows";
#elif NET10_0
    private const string Framework = ":net10";
#else
#error Unexpected target framework.
#endif
}

internal sealed class AnalyzerMarker
{
}
