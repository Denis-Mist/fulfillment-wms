namespace Wms.Domain.Common;

/// <summary>Округление как Math.round в JS (половина — вверх), чтобы числа совпадали с моками.</summary>
public static class JsMath
{
    public static double Round(double value) => Math.Floor(value + 0.5);
    public static double Round1(double value) => Math.Floor(value * 10 + 0.5) / 10;
}
