using SymbolContext.Product;

var service = new LedgerService();
if (service.TryFormat(null, out var nullResult)
    || nullResult != string.Empty
    || service.TryFormat(string.Empty, out var emptyResult)
    || emptyResult != string.Empty
    || service.TryFormat("   ", out var whitespaceResult)
    || whitespaceResult != string.Empty)
{
    return Fail("TryFormat must reject null, empty, and whitespace input.");
}

if (!service.TryFormat("  entry  ", out var formatted)
    || formatted != "ledger:entry"
    || service.Name != "ledger"
    || service.Format("entry") != "ledger:entry")
{
    return Fail("TryFormat success or existing behavior is incorrect.");
}

Console.WriteLine("repository-task validation passed");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
