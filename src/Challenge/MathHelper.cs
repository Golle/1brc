using System.Runtime.CompilerServices;

namespace Challenge;

public static class MathHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Min(int a, int b)
        => b + ((a - b) & (a - b) >> 31);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Max(int a, int b)
        => a - ((a - b) & (a - b) >> 31);
}
