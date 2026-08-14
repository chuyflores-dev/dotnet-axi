using SymbolContext.Worker;

var renamed = new WorkerScopeProbe();
var assembly = renamed.GetType().Assembly;
if (renamed.GetType().FullName != "SymbolContext.Worker.WorkerScopeProbe"
    || assembly.GetType("SymbolContext.Worker.ScopeProbe") is not null)
{
    Console.Error.WriteLine(
        "Only the Worker-owned ScopeProbe must be renamed to WorkerScopeProbe.");
    return 1;
}

Console.WriteLine("repository-task validation passed");
return 0;
