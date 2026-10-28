namespace Wms.Application.Common;

/// <summary>Детерминированный генератор (mulberry32) для синтетики, воспроизводимой от ключа.</summary>
public sealed class SeededRandom
{
    private uint _state;

    public SeededRandom(uint seed) => _state = seed;

    public SeededRandom(string key) : this(Hash(key)) { }

    public static uint Hash(string key, uint start = 7)
    {
        unchecked
        {
            var h = start;
            foreach (var ch in key) h = h * 31 + ch;
            return h;
        }
    }

    public double Next()
    {
        unchecked
        {
            _state += 0x6D2B79F5;
            var t = _state;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return ((t ^ (t >> 14)) >> 0) / 4294967296.0;
        }
    }

    public int Int(int min, int max) => min + (int)Math.Floor(Next() * (max - min + 1));

    public double Float(double min, double max) => min + Next() * (max - min);

    public bool Bool(double p) => Next() < p;
}
