# Embed Prolog in C#

Use an embedded engine when a .NET application needs to consult source and query answers at run
time. Install the .NET 10 SDK and create a console application in your projects directory:

```console
dotnet new console -n PrologHostApp --framework net10.0
dotnet add PrologHostApp/PrologHostApp.csproj package DotProlog.Compiler
```

Replace `PrologHostApp/Program.cs` with:

```csharp
using DotProlog.Compiler;
using DotProlog.Runtime;

var engine = new PrologEngine(PrologLanguageMode.StrictIso);
var loaded = engine.ConsultText("colour(red). colour(green). colour(blue).");
if (!loaded.Success)
{
    foreach (var diagnostic in loaded.Diagnostics)
    {
        Console.Error.WriteLine(diagnostic);
    }

    return;
}

foreach (PrologSolution solution in engine.Query("colour(C)").Solutions())
{
    Console.WriteLine(solution["C"]);
}
```

Run the host:

```console
dotnet run --project PrologHostApp/PrologHostApp.csproj
```

The output should contain `red`, `green`, and `blue`, one per line. Substitute your own source
and goal once the host works. Check consultation diagnostics before querying.

Finish enumerating or dispose the current query before starting another goal on that engine.
Use separate engines for independent concurrent callers. See the
[embedding reference](../dotnet-integration.md#embed-the-engine) for value lifetimes and mode selection.

For repeated calls with .NET values, use [predicate binding](../dotnet-integration.md#bind-a-predicate).
For generated methods such as `pricing.Discount(100.0, 15)`, follow
[create a typed Prolog library](create-library.md).
