namespace SymbolContext.Product;

/// <summary>Formats ledger values with deterministic fixture context.</summary>
public sealed class LedgerService
{
    public string Name => "ledger";

    public string Format(string value)
    {
        return $"ledger:{value}";
    }
}
