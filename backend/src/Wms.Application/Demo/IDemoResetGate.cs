namespace Wms.Application.Demo;

/// <summary>Эксклюзивный флаг «идёт сброс демо-данных» — для 503 на /api и защиты от параллельного сброса.</summary>
public interface IDemoResetGate
{
    bool IsResetting { get; }
    bool TryBegin();
    void End();
}
