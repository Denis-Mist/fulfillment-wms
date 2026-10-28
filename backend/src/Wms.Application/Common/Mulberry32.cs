namespace Wms.Application.Common;

/// <summary>
/// Детерминированный PRNG — побитовый порт createRandom() из frontend/src/mocks/lib/random.ts,
/// чтобы витринные данные ФЦ совпадали с моками.
/// </summary>
public sealed class Mulberry32(uint seed)
{
    private uint _state = seed;

    public double Next()
    {
        unchecked
        {
            _state += 0x6d2b79f5;
            var s = _state;
            var t = (uint)((int)(s ^ (s >> 15)) * (int)(1 | s));
            t = (t + (uint)((int)(t ^ (t >> 7)) * (int)(61 | t))) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    public int Int(int min, int max) => (int)Math.Floor(Next() * (max - min + 1)) + min;
    public double Float(double min, double max) => Next() * (max - min) + min;
    public bool Bool(double probability = 0.5) => Next() < probability;
    public T Pick<T>(IReadOnlyList<T> items) => items[(int)Math.Floor(Next() * items.Count)];

    /// <summary>seedOf() из моков: s*31 + charCode в double, затем &gt;&gt;&gt; 0.</summary>
    public static uint SeedOf(string id)
    {
        double s = 7;
        foreach (var ch in id) s = s * 31 + ch;
        var m = Math.Truncate(s) % 4294967296.0;
        if (m < 0) m += 4294967296.0;
        return (uint)m;
    }
}
